using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Sacred.Core.Assets;

public sealed record GrnAsset(
    string Name,
    byte[] RawBytes,
    string? ReferencedTexture,
    Mesh? Mesh
);

public static class GrnAssetLoader
{
    private const bool EnableHeuristicMeshExtraction = false;

    public static GrnAsset LoadFromBytes(
        string name,
        byte[] bytes,
        GrnMeshExtractionMode meshExtractionMode = GrnMeshExtractionMode.PrimarySlice)
    {
        var ascii = Encoding.ASCII.GetString(bytes.Where(static b => b is >= 32 and < 127).ToArray());
        var referencedTexture = ascii.Contains("shield_tower.tga", StringComparison.OrdinalIgnoreCase)
            ? "shield_tower.tga"
            : null;

        // The current extractor is intentionally disabled: broad binary heuristics can find plausible
        // float/index blocks that are not the actual Granny mesh, producing slow loads and bad geometry.
        var mesh = Granny1MeshExtractor.TryExtract(bytes, meshExtractionMode);

        return new GrnAsset(name, bytes, referencedTexture, mesh);
    }

    public static GrnAsset LoadCharacterFromBytes(
        string name,
        byte[] baseBytes,
        IReadOnlyList<byte[]> attachmentBytes,
        IReadOnlySet<string>? hiddenBaseTextureNames = null)
    {
        var mesh = Granny1MeshExtractor.TryExtractCharacter(
            baseBytes,
            attachmentBytes,
            hiddenBaseTextureNames);
        return new GrnAsset(name, baseBytes, null, mesh);
    }
}

public readonly record struct VertexPositionNormalTexture(Vector3 Position, Vector3 Normal, Vector2 TexCoord);

public sealed record Mesh(VertexPositionNormalTexture[] Vertices, ushort[] Indices)
{
    public IReadOnlyList<MeshSurface> Surfaces { get; init; } = [];
}

public readonly record struct MeshSurface(int IndexStart, int IndexCount, string? TextureName);

public enum GrnMeshExtractionMode
{
    PrimarySlice,
    CompositeSlices
}

public static class Granny1MeshExtractor
{
    private const int HeaderSize = 0x40;
    private const int ItemListHeaderSize = 0x10;
    private const uint MainChunk = 0xCA5E0000;
    private const uint FinalChunk = 0xCA5E0101;
    private const uint CopyrightChunk = 0xCA5E0102;
    private const uint ObjectChunk = 0xCA5E0103;
    private const uint TextChunk = 0xCA5E0200;
    private const uint TextureInfoChunk = 0xCA5E0301;
    private const uint TextureInfosChunk = 0xCA5E0304;
    private const uint BoneChunk = 0xCA5E0506;
    private const uint BoneListChunk = 0xCA5E0507;
    private const uint MeshChunk = 0xCA5E0601;
    private const uint PointChunk = 0xCA5E0801;
    private const uint NormalChunk = 0xCA5E0802;
    private const uint TexturePointChunk = 0xCA5E0803;
    private const uint WeightChunk = 0xCA5E0702;
    private const uint PolygonChunk = 0xCA5E0901;
    private const uint MeshIdChunk = 0xCA5E0F04;
    private const uint MaterialListChunk = 0xCA5E0D01;
    private const uint MaterialChunk = 0xCA5E0D00;
    private const uint MaterialTextureSlotChunk = 0xCA5E0D03;
    private const uint BoneObjectListChunk = 0xCA5E0B01;
    private const uint BoneTieListChunk = 0xCA5E0C01;
    private const uint BoneObjectPointerChunk = 0xCA5E0C02;
    private const uint BoneObjectPointerEndChunk = 0xCA5E0C05;
    private const uint BoneTieChunk = 0xCA5E0C0A;
    private const uint TextureListChunk = 0xCA5E0E01;
    private const uint TexturePolygonGroupChunk = 0xCA5E0E02;
    private const uint TexturePolygonChunk = 0xCA5E0E06;
    private const uint ObjectListChunk = 0xCA5E0F03;
    private const uint ObjectDataChunk = 0xCA5E0F00;
    private const uint ObjectKeyChunk = 0xCA5E0F01;
    private const uint ObjectValueChunk = 0xCA5E0F02;
    private const int DescriptorSize = 12;
    private const int TextureCoordinateStride = 12;
    private const int MaximumMeshChildDescriptors = 128;
    private const int MaximumMeshVertices = ushort.MaxValue;
    private const float TargetHeight = 145.0f;

    public static Mesh? TryExtract(
        ReadOnlySpan<byte> data,
        GrnMeshExtractionMode extractionMode = GrnMeshExtractionMode.PrimarySlice)
    {
        var slices = ExtractSlices(data);

        if (slices.Count == 0)
            return null;

        return extractionMode == GrnMeshExtractionMode.CompositeSlices
            ? BuildMesh(slices)
            : BuildMesh([SelectPrimarySlice(slices)]);
    }

    public static Mesh? TryExtractCharacter(
        ReadOnlySpan<byte> baseData,
        IReadOnlyList<byte[]> attachmentData,
        IReadOnlySet<string>? hiddenBaseTextureNames = null)
    {
        var baseSlices = ExtractSlices(baseData);
        if (baseSlices.Count == 0)
            return null;

        var baseSlice = SelectPrimarySlice(baseSlices);
        if (hiddenBaseTextureNames is { Count: > 0 })
        {
            baseSlice = baseSlice with
            {
                HiddenTextureNames = new HashSet<string>(
                    hiddenBaseTextureNames,
                    StringComparer.OrdinalIgnoreCase)
            };
        }

        var slices = new List<ParsedMeshSlice> { baseSlice };
        foreach (var bytes in attachmentData)
        {
            var attachmentSlices = ExtractSlices(bytes);
            if (attachmentSlices.Count == 0)
                continue;

            var attachment = RetargetSlice(SelectPrimarySlice(attachmentSlices), baseSlice.Skeleton);
            if (attachment is not null)
                slices.Add(attachment);
        }

        return BuildMesh(slices);
    }

    private static List<ParsedMeshSlice> ExtractSlices(ReadOnlySpan<byte> data)
    {
        var sliceStarts = FindGrannySliceStarts(data);
        var slices = new List<ParsedMeshSlice>();
        foreach (var (start, end) in EnumerateSlices(sliceStarts, data.Length))
        {
            var slice = TryExtractSlice(data[start..], end - start);
            if (slice is not null)
                slices.Add(slice);
        }

        return slices;
    }

    private static ParsedMeshSlice SelectPrimarySlice(IReadOnlyList<ParsedMeshSlice> slices)
    {
        var best = slices[0];
        var bestPolygonCount = CountPolygons(best.Parts);
        for (var i = 1; i < slices.Count; i++)
        {
            var polygonCount = CountPolygons(slices[i].Parts);
            if (polygonCount <= bestPolygonCount)
                continue;

            best = slices[i];
            bestPolygonCount = polygonCount;
        }

        return best;
    }

    private static ParsedMeshSlice? TryExtractSlice(ReadOnlySpan<byte> data, int descriptorScanLength)
    {
        if (data.Length < HeaderSize + 8 || ReadUInt32(data, HeaderSize) != MainChunk)
            return null;

        var mainOffset = HeaderSize + 4;
        var childCount = ReadUInt32(data, mainOffset);
        if (childCount == 0 || childCount > 16)
            return null;

        var position = mainOffset + 4 + 24;
        ParsedMeshSlice? best = null;
        for (var child = 0; child < childCount; child++)
        {
            if (position + 20 > data.Length)
                return best;

            var chunk = ReadUInt32(data, position);
            var listOffset = ReadUInt32(data, position + 8);
            position += 20;

            if (chunk != ObjectChunk)
                continue;

            var slice = TryExtractItemList(data, checked((int)listOffset), descriptorScanLength);
            if (slice is not null && (best is null || CountPolygons(slice.Parts) > CountPolygons(best.Parts)))
                best = slice;
        }

        return best;
    }

    private static ParsedMeshSlice? TryExtractItemList(ReadOnlySpan<byte> data, int listOffset, int descriptorScanLength)
    {
        if (listOffset < 0 || listOffset + ItemListHeaderSize > data.Length)
            return null;

        var descriptors = ReadItemDescriptors(data, listOffset, descriptorScanLength);
        var texturePolygonBlocks = ReadTexturePolygonBlocks(data, listOffset, descriptorScanLength, descriptors);
        var texturePolygons = texturePolygonBlocks.SelectMany(static block => block.Polygons).ToArray();
        var parts = new List<ParsedMeshPart>();
        var seenMeshes = new HashSet<MeshKey>();
        foreach (var descriptor in descriptors)
        {
            if (descriptor.Chunk != MeshChunk)
                continue;

            if (!TryReadMeshPart(data, descriptor.DescriptorOffset, listOffset, descriptorScanLength, out var part))
                continue;

            if (!seenMeshes.Add(new MeshKey(part.PointOffset, part.PolygonOffset)))
                continue;

            parts.Add(part);
        }

        return parts.Count == 0
            ? null
            : new ParsedMeshSlice(
                parts.ToArray(),
                texturePolygons,
                texturePolygonBlocks.ToArray(),
                ReadMaterialTextureNames(data, descriptorScanLength, descriptors),
                ReadSkeleton(data, descriptorScanLength, descriptors),
                null);
    }

    private static bool TryReadMeshPart(ReadOnlySpan<byte> data, int meshDescriptorOffset, int listBase, int descriptorScanLength, out ParsedMeshPart part)
    {
        part = default;

        var childCount = ReadUInt32(data, meshDescriptorOffset + 8);
        if (childCount == 0 || childCount > MaximumMeshChildDescriptors)
            return false;

        var pointOffset = -1;
        var normalOffset = -1;
        var textureOffset = -1;
        var weightOffset = -1;
        var polygonOffset = -1;
        var meshIdOffset = -1;
        var descriptorOffset = meshDescriptorOffset + DescriptorSize;

        for (var child = 0; child < childCount; child++)
        {
            if (descriptorOffset + DescriptorSize > descriptorScanLength)
                return false;

            var chunk = ReadUInt32(data, descriptorOffset);
            var absoluteOffset = AddOffset(listBase, ReadUInt32(data, descriptorOffset + 4));
            switch (chunk)
            {
                case PointChunk:
                    pointOffset = absoluteOffset;
                    break;
                case NormalChunk:
                    normalOffset = absoluteOffset;
                    break;
                case TexturePointChunk:
                    textureOffset = textureOffset < 0 ? absoluteOffset : textureOffset;
                    break;
                case WeightChunk:
                    weightOffset = absoluteOffset;
                    break;
                case PolygonChunk:
                    polygonOffset = absoluteOffset;
                    break;
                case MeshIdChunk:
                    meshIdOffset = absoluteOffset;
                    break;
            }

            descriptorOffset += DescriptorSize;
        }

        if (!OffsetsAreValid(data.Length, pointOffset, normalOffset, textureOffset, weightOffset, polygonOffset, meshIdOffset))
            return false;

        var pointCount = (normalOffset - pointOffset) / 12;
        var textureCount = (weightOffset - textureOffset - 4) / TextureCoordinateStride;
        var polygonCount = (meshIdOffset - polygonOffset) / 24;
        if (pointCount <= 0 || textureCount < 0 || polygonCount <= 0)
            return false;

        var positions = new Vector3[pointCount];
        for (var i = 0; i < positions.Length; i++)
        {
            var offset = pointOffset + i * 12;
            positions[i] = new Vector3(ReadSingle(data, offset), ReadSingle(data, offset + 4), ReadSingle(data, offset + 8));
            if (!IsFinite(positions[i]))
                return false;
        }

        var texCoords = new Vector2[textureCount];
        var textureDataOffset = textureOffset + 4;
        for (var i = 0; i < texCoords.Length; i++)
        {
            var offset = textureDataOffset + i * TextureCoordinateStride;
            texCoords[i] = new Vector2(ReadSingle(data, offset), ReadSingle(data, offset + 4));
        }

        var polygons = new GrannyPolygon[polygonCount];
        for (var i = 0; i < polygons.Length; i++)
        {
            var offset = polygonOffset + i * 24;
            polygons[i] = new GrannyPolygon(
                ReadUInt32(data, offset),
                ReadUInt32(data, offset + 4),
                ReadUInt32(data, offset + 8));
        }

        part = new ParsedMeshPart(
            pointOffset,
            polygonOffset,
            positions,
            texCoords,
            polygons,
            ReadVertexWeights(data, weightOffset, polygonOffset, pointCount));
        return true;
    }

    private static VertexWeight[][] ReadVertexWeights(
        ReadOnlySpan<byte> data,
        int weightOffset,
        int polygonOffset,
        int pointCount)
    {
        var weights = new VertexWeight[pointCount][];
        Array.Fill(weights, []);
        if (weightOffset < 0 || weightOffset + 12 > polygonOffset || polygonOffset > data.Length)
            return weights;

        var weightCountValue = ReadUInt32(data, weightOffset);
        if (weightCountValue > pointCount)
            return weights;

        var position = weightOffset + 12;
        for (var vertexIndex = 0; vertexIndex < weightCountValue; vertexIndex++)
        {
            if (position + 4 > polygonOffset)
                return weights;

            var boneCountValue = ReadUInt32(data, position);
            position += 4;
            if (boneCountValue > 32 || position + boneCountValue * 8L > polygonOffset)
                return weights;

            var vertexWeights = new VertexWeight[boneCountValue];
            for (var boneIndex = 0; boneIndex < vertexWeights.Length; boneIndex++)
            {
                vertexWeights[boneIndex] = new VertexWeight(
                    ReadUInt32(data, position),
                    ReadSingle(data, position + 4));
                position += 8;
            }

            weights[vertexIndex] = vertexWeights;
        }

        return weights;
    }

    private static GrannySkeleton? ReadSkeleton(
        ReadOnlySpan<byte> data,
        int descriptorScanLength,
        IReadOnlyList<ItemDescriptor> descriptors)
    {
        var boneListIndex = FindImmediateChild(descriptors, -1, BoneListChunk);
        if (boneListIndex < 0)
            return null;

        var boneDescriptors = new List<ItemDescriptor>();
        var boneListEnd = DescendantEndExclusive(descriptors, boneListIndex);
        for (var descriptorIndex = boneListIndex + 1; descriptorIndex < boneListEnd; descriptorIndex++)
        {
            if (descriptors[descriptorIndex].Chunk == BoneChunk)
                boneDescriptors.Add(descriptors[descriptorIndex]);
        }

        if (boneDescriptors.Count == 0)
            return null;

        var textEntries = ReadTextEntries(data, descriptorScanLength, descriptors);
        var objects = ReadObjects(data, descriptors);
        var objectNameKey = FindStringIndex(textEntries, "__ObjectName");
        var boneObjectIds = ReadBoneObjectIds(data, descriptors);
        var boneObjectPointers = ReadBoneObjectPointers(data, descriptors);
        var boneTieBones = ReadBoneTieBones(data, descriptors);

        var localTransforms = new Matrix4x4[boneDescriptors.Count];
        var parents = new int[boneDescriptors.Count];
        var names = new string[boneDescriptors.Count];
        for (var boneIndex = 0; boneIndex < boneDescriptors.Count; boneIndex++)
        {
            var dataOffset = boneDescriptors[boneIndex].DataOffset;
            if (dataOffset < 0 || dataOffset + 32 > data.Length)
                return null;

            var parentValue = ReadUInt32(data, dataOffset);
            parents[boneIndex] = parentValue < boneDescriptors.Count
                ? (int)parentValue
                : boneIndex;

            var translation = new Vector3(
                ReadSingle(data, dataOffset + 4),
                ReadSingle(data, dataOffset + 8),
                ReadSingle(data, dataOffset + 12));
            var rotation = new Quaternion(
                ReadSingle(data, dataOffset + 16),
                ReadSingle(data, dataOffset + 20),
                ReadSingle(data, dataOffset + 24),
                ReadSingle(data, dataOffset + 28));
            if (!IsFinite(translation) || !IsFinite(rotation))
                return null;

            localTransforms[boneIndex] = CreateBoneTransform(rotation, translation);
            names[boneIndex] = ResolveBoneName(
                boneIndex,
                objectNameKey,
                textEntries,
                objects,
                boneObjectIds,
                boneObjectPointers);
        }

        var worldTransforms = new Matrix4x4[boneDescriptors.Count];
        var transformStates = new byte[boneDescriptors.Count];
        for (var boneIndex = 0; boneIndex < boneDescriptors.Count; boneIndex++)
        {
            if (!TryComputeBoneWorldTransform(
                    boneIndex,
                    parents,
                    localTransforms,
                    worldTransforms,
                    transformStates))
                return null;
        }

        var bones = new GrannyBone[boneDescriptors.Count];
        var bonesByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            bones[boneIndex] = new GrannyBone(names[boneIndex], worldTransforms[boneIndex]);
            if (!string.IsNullOrWhiteSpace(names[boneIndex]))
                bonesByName.TryAdd(names[boneIndex], boneIndex);
        }

        return new GrannySkeleton(bones, boneTieBones, bonesByName);
    }

    private static uint[] ReadBoneObjectIds(
        ReadOnlySpan<byte> data,
        IReadOnlyList<ItemDescriptor> descriptors)
    {
        var listIndex = FindImmediateChild(descriptors, -1, BoneObjectListChunk);
        if (listIndex < 0)
            return [];

        var objectIds = new List<uint>();
        var end = DescendantEndExclusive(descriptors, listIndex);
        for (var descriptorIndex = listIndex + 1; descriptorIndex < end; descriptorIndex++)
        {
            var descriptor = descriptors[descriptorIndex];
            if (descriptor.Chunk == MeshIdChunk &&
                descriptor.DataOffset >= 0 &&
                descriptor.DataOffset + 4 <= data.Length)
                objectIds.Add(ReadUInt32(data, descriptor.DataOffset));
        }

        return objectIds.ToArray();
    }

    private static uint[] ReadBoneObjectPointers(
        ReadOnlySpan<byte> data,
        IReadOnlyList<ItemDescriptor> descriptors)
    {
        var listIndex = FindImmediateChild(descriptors, -1, BoneTieListChunk);
        if (listIndex < 0)
            return [];

        var startOffset = -1;
        var endOffset = -1;
        var end = DescendantEndExclusive(descriptors, listIndex);
        for (var descriptorIndex = listIndex + 1; descriptorIndex < end; descriptorIndex++)
        {
            var descriptor = descriptors[descriptorIndex];
            if (descriptor.Chunk == BoneObjectPointerChunk)
            {
                startOffset = descriptor.DataOffset;
            }
            else if (descriptor.Chunk == BoneObjectPointerEndChunk && startOffset >= 0)
            {
                endOffset = descriptor.DataOffset;
                break;
            }
        }

        if (startOffset < 0 ||
            endOffset <= startOffset ||
            endOffset > data.Length ||
            (endOffset - startOffset) % 4 != 0)
            return [];

        var pointers = new uint[(endOffset - startOffset) / 4];
        for (var pointerIndex = 0; pointerIndex < pointers.Length; pointerIndex++)
            pointers[pointerIndex] = ReadUInt32(data, startOffset + pointerIndex * 4);

        return pointers;
    }

    private static uint[] ReadBoneTieBones(
        ReadOnlySpan<byte> data,
        IReadOnlyList<ItemDescriptor> descriptors)
    {
        var listIndex = FindImmediateChild(descriptors, -1, BoneTieListChunk);
        if (listIndex < 0)
            return [];

        var bones = new List<uint>();
        var end = DescendantEndExclusive(descriptors, listIndex);
        for (var descriptorIndex = listIndex + 1; descriptorIndex < end; descriptorIndex++)
        {
            var descriptor = descriptors[descriptorIndex];
            if (descriptor.Chunk == BoneTieChunk &&
                descriptor.DataOffset >= 0 &&
                descriptor.DataOffset + 4 <= data.Length)
                bones.Add(ReadUInt32(data, descriptor.DataOffset));
        }

        return bones.ToArray();
    }

    private static string ResolveBoneName(
        int boneIndex,
        int objectNameKey,
        IReadOnlyList<string> textEntries,
        IReadOnlyList<Dictionary<uint, uint>> objects,
        IReadOnlyList<uint> boneObjectIds,
        IReadOnlyList<uint> boneObjectPointers)
    {
        if (objectNameKey < 0 || boneIndex >= boneObjectPointers.Count)
            return string.Empty;

        var objectPointer = boneObjectPointers[boneIndex];
        if (objectPointer == 0 || objectPointer > boneObjectIds.Count)
            return string.Empty;

        var objectId = boneObjectIds[(int)objectPointer - 1];
        if (objectId == 0 || objectId > objects.Count)
            return string.Empty;

        var boneObject = objects[(int)objectId - 1];
        if (!boneObject.TryGetValue((uint)objectNameKey, out var textId) || textId >= textEntries.Count)
            return string.Empty;

        return textEntries[(int)textId];
    }

    private static Matrix4x4 CreateBoneTransform(Quaternion rotation, Vector3 translation)
    {
        var normalizedRotation = rotation.LengthSquared() > 0.000001f
            ? Quaternion.Normalize(rotation)
            : Quaternion.Identity;
        return Matrix4x4.CreateFromQuaternion(normalizedRotation) *
               Matrix4x4.CreateTranslation(translation);
    }

    private static bool TryComputeBoneWorldTransform(
        int boneIndex,
        IReadOnlyList<int> parents,
        IReadOnlyList<Matrix4x4> localTransforms,
        Matrix4x4[] worldTransforms,
        byte[] transformStates)
    {
        if (transformStates[boneIndex] == 2)
            return true;
        if (transformStates[boneIndex] == 1)
            return false;

        transformStates[boneIndex] = 1;
        var parentIndex = parents[boneIndex];
        if (parentIndex == boneIndex)
        {
            worldTransforms[boneIndex] = localTransforms[boneIndex];
        }
        else
        {
            if ((uint)parentIndex >= (uint)parents.Count ||
                !TryComputeBoneWorldTransform(
                    parentIndex,
                    parents,
                    localTransforms,
                    worldTransforms,
                    transformStates))
                return false;

            worldTransforms[boneIndex] = localTransforms[boneIndex] * worldTransforms[parentIndex];
        }

        transformStates[boneIndex] = 2;
        return true;
    }

    private static ParsedMeshSlice? RetargetSlice(
        ParsedMeshSlice slice,
        GrannySkeleton? targetSkeleton)
    {
        var sourceSkeleton = slice.Skeleton;
        if (sourceSkeleton is null || targetSkeleton is null)
            return null;

        var tieTransforms = new Matrix4x4?[sourceSkeleton.BoneTieBones.Length];
        var mappedTieCount = 0;
        for (var tieIndex = 0; tieIndex < sourceSkeleton.BoneTieBones.Length; tieIndex++)
        {
            var sourceBoneIndex = sourceSkeleton.BoneTieBones[tieIndex];
            if (sourceBoneIndex >= sourceSkeleton.Bones.Length)
                continue;

            var sourceBone = sourceSkeleton.Bones[sourceBoneIndex];
            if (string.IsNullOrWhiteSpace(sourceBone.Name) ||
                !targetSkeleton.BonesByName.TryGetValue(sourceBone.Name, out var targetBoneIndex) ||
                !Matrix4x4.Invert(sourceBone.RestWorld, out var inverseSourceRest))
                continue;

            // System.Numerics transforms row vectors, so the column-vector Granny skinning order is reversed.
            tieTransforms[tieIndex] = inverseSourceRest * targetSkeleton.Bones[targetBoneIndex].RestWorld;
            mappedTieCount++;
        }

        if (mappedTieCount == 0)
            return null;

        var parts = new ParsedMeshPart[slice.Parts.Length];
        for (var partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            var part = slice.Parts[partIndex];
            var positions = new Vector3[part.Positions.Length];
            for (var vertexIndex = 0; vertexIndex < positions.Length; vertexIndex++)
            {
                var sourcePosition = part.Positions[vertexIndex];
                var transformedPosition = Vector3.Zero;
                var totalWeight = 0.0f;
                foreach (var weight in part.Weights[vertexIndex])
                {
                    if (weight.BoneTieIndex >= tieTransforms.Length ||
                        tieTransforms[weight.BoneTieIndex] is not { } transform ||
                        !float.IsFinite(weight.Weight) ||
                        weight.Weight <= 0.0f)
                        continue;

                    transformedPosition += Vector3.Transform(sourcePosition, transform) * weight.Weight;
                    totalWeight += weight.Weight;
                }

                positions[vertexIndex] = totalWeight > 0.000001f
                    ? transformedPosition / totalWeight
                    : sourcePosition;
            }

            parts[partIndex] = part with { Positions = positions };
        }

        return slice with
        {
            Parts = parts,
            HiddenTextureNames = null
        };
    }

    private static Mesh BuildMesh(IReadOnlyList<ParsedMeshSlice> slices)
    {
        var allParts = slices.SelectMany(static slice => slice.Parts).ToArray();
        var bounds = CalculateBounds(allParts);
        var axis = VerticalAxis(bounds.Max - bounds.Min);
        var horizontal0 = (axis + 1) % 3;
        var horizontal1 = (axis + 2) % 3;
        var center = (bounds.Min + bounds.Max) * 0.5f;
        var verticalSpan = Math.Max(1.0f, Axis(bounds.Max - bounds.Min, axis));
        var scale = TargetHeight / verticalSpan;

        var vertices = new List<VertexPositionNormalTexture>();
        var indices = new List<ushort>();
        var surfaces = new List<MeshSurface>();

        foreach (var slice in slices)
        {
            var sliceIndexStart = indices.Count;
            var sliceSurfaces = BuildTexturedMeshFromBlocks(
                slice.Parts,
                slice.TexturePolygonBlocks,
                slice.TextureNames,
                slice.HiddenTextureNames,
                bounds.Min,
                center,
                axis,
                horizontal0,
                horizontal1,
                scale,
                vertices,
                indices);

            if (indices.Count == sliceIndexStart)
                sliceSurfaces = BuildSequentialMesh(
                    slice.Parts,
                    slice.TexturePolygons,
                    slice.TexturePolygonBlocks,
                    slice.TextureNames,
                    bounds.Min,
                    center,
                    axis,
                    horizontal0,
                    horizontal1,
                    scale,
                    vertices,
                    indices);

            surfaces.AddRange(sliceSurfaces);
        }

        var vertexArray = vertices.ToArray();
        var indexArray = indices.ToArray();
        GrnStaticMeshExtractor.RecalculateNormals(vertexArray, indexArray);
        return new Mesh(vertexArray, indexArray)
        {
            Surfaces = ClipSurfaceRanges(surfaces, indexArray.Length)
        };
    }

    private static int CountPolygons(IEnumerable<ParsedMeshPart> parts) =>
        parts.Sum(static part => part.Polygons.Length);

    private static List<MeshSurface> BuildTexturedMeshFromBlocks(
        IReadOnlyList<ParsedMeshPart> parts,
        IReadOnlyList<TexturePolygonBlock> texturePolygonBlocks,
        IReadOnlyList<string> textureNames,
        IReadOnlySet<string>? hiddenTextureNames,
        Vector3 min,
        Vector3 center,
        int verticalAxis,
        int horizontalAxis0,
        int horizontalAxis1,
        float scale,
        List<VertexPositionNormalTexture> vertices,
        List<ushort> indices)
    {
        var surfaces = new List<MeshSurface>(texturePolygonBlocks.Count);
        if (texturePolygonBlocks.Count == 0)
            return surfaces;

        var usedPolygons = parts.Select(static part => new HashSet<uint>()).ToArray();
        var blockPartAssignments = AssignTextureBlocksToParts(parts, texturePolygonBlocks);
        for (var blockIndex = 0; blockIndex < texturePolygonBlocks.Count; blockIndex++)
        {
            var block = texturePolygonBlocks[blockIndex];
            var partIndex = blockPartAssignments[blockIndex] >= 0
                ? blockPartAssignments[blockIndex]
                : BlockFitsPart(parts, block.MeshIndex, block)
                ? block.MeshIndex
                : SelectTextureBlockPart(parts, block, usedPolygons);
            if (partIndex < 0)
                continue;

            var part = parts[partIndex];
            var textureName = TextureNameForBlock(block, blockIndex, textureNames);
            var hidden = textureName is not null && hiddenTextureNames?.Contains(textureName) == true;
            var indexStart = indices.Count;
            foreach (var texturePolygon in block.Polygons)
            {
                if (!hidden && vertices.Count + 3 > MaximumMeshVertices)
                    break;

                if (texturePolygon.A >= part.Polygons.Length ||
                    texturePolygon.B >= part.TextureCoordinates.Length ||
                    texturePolygon.C >= part.TextureCoordinates.Length ||
                    texturePolygon.D >= part.TextureCoordinates.Length)
                    continue;

                if (!usedPolygons[partIndex].Add(texturePolygon.A))
                    continue;

                if (hidden)
                    continue;

                var polygon = part.Polygons[checked((int)texturePolygon.A)];
                if (!IsValidPolygon(part, polygon))
                    continue;

                AddCorner(part, polygon.A, texturePolygon.B, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, scale, vertices, indices);
                AddCorner(part, polygon.B, texturePolygon.C, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, scale, vertices, indices);
                AddCorner(part, polygon.C, texturePolygon.D, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, scale, vertices, indices);
            }

            var indexCount = indices.Count - indexStart;
            if (indexCount > 0)
                surfaces.Add(new MeshSurface(
                    indexStart,
                    indexCount,
                    textureName));
        }

        var untexturedIndexStart = indices.Count;
        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var used = usedPolygons[partIndex];
            for (var polygonIndex = 0; polygonIndex < part.Polygons.Length; polygonIndex++)
            {
                if (vertices.Count + 3 > MaximumMeshVertices)
                    break;

                if (used.Contains((uint)polygonIndex))
                    continue;

                var polygon = part.Polygons[polygonIndex];
                if (!IsValidPolygon(part, polygon))
                    continue;

                AddCorner(part, polygon.A, uint.MaxValue, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, scale, vertices, indices);
                AddCorner(part, polygon.B, uint.MaxValue, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, scale, vertices, indices);
                AddCorner(part, polygon.C, uint.MaxValue, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, scale, vertices, indices);
            }
        }

        var untexturedIndexCount = indices.Count - untexturedIndexStart;
        if (untexturedIndexCount > 0)
            surfaces.Add(new MeshSurface(untexturedIndexStart, untexturedIndexCount, null));

        return surfaces;
    }

    private static bool BlockFitsPart(
        IReadOnlyList<ParsedMeshPart> parts,
        int partIndex,
        TexturePolygonBlock block)
    {
        if (partIndex < 0 || partIndex >= parts.Count)
            return false;

        var part = parts[partIndex];
        foreach (var texturePolygon in block.Polygons)
        {
            if (texturePolygon.A >= part.Polygons.Length ||
                texturePolygon.B >= part.TextureCoordinates.Length ||
                texturePolygon.C >= part.TextureCoordinates.Length ||
                texturePolygon.D >= part.TextureCoordinates.Length)
                return false;
        }

        return true;
    }

    private static string? TextureNameForBlock(
        TexturePolygonBlock block,
        int blockIndex,
        IReadOnlyList<string> textureNames)
    {
        var textureIndex = block.TextureIndex > 0
            ? block.TextureIndex - 1
            : blockIndex;
        return textureIndex >= 0 && textureIndex < textureNames.Count
            ? textureNames[textureIndex]
            : null;
    }

    private static int[] AssignTextureBlocksToParts(
        IReadOnlyList<ParsedMeshPart> parts,
        IReadOnlyList<TexturePolygonBlock> texturePolygonBlocks)
    {
        var assignments = Enumerable.Repeat(-1, texturePolygonBlocks.Count).ToArray();
        var usedParts = new bool[parts.Count];
        for (var groupStart = 0; groupStart < texturePolygonBlocks.Count;)
        {
            var polygonCount = 0;
            var assignedPart = -1;
            var groupEnd = -1;
            for (var blockIndex = groupStart; blockIndex < texturePolygonBlocks.Count; blockIndex++)
            {
                polygonCount += texturePolygonBlocks[blockIndex].Polygons.Length;
                for (var partIndex = 0; partIndex < parts.Count; partIndex++)
                {
                    if (usedParts[partIndex] || parts[partIndex].Polygons.Length != polygonCount)
                        continue;

                    if (!TextureBlockGroupFitsPart(parts[partIndex], texturePolygonBlocks, groupStart, blockIndex))
                        continue;

                    assignedPart = partIndex;
                    groupEnd = blockIndex;
                    break;
                }

                if (assignedPart >= 0)
                    break;
            }

            if (assignedPart < 0)
            {
                groupStart++;
                continue;
            }

            usedParts[assignedPart] = true;
            for (var blockIndex = groupStart; blockIndex <= groupEnd; blockIndex++)
                assignments[blockIndex] = assignedPart;
            groupStart = groupEnd + 1;
        }

        return assignments;
    }

    private static bool TextureBlockGroupFitsPart(
        ParsedMeshPart part,
        IReadOnlyList<TexturePolygonBlock> texturePolygonBlocks,
        int groupStart,
        int groupEnd)
    {
        for (var blockIndex = groupStart; blockIndex <= groupEnd; blockIndex++)
        {
            foreach (var texturePolygon in texturePolygonBlocks[blockIndex].Polygons)
            {
                if (texturePolygon.A >= part.Polygons.Length ||
                    texturePolygon.B >= part.TextureCoordinates.Length ||
                    texturePolygon.C >= part.TextureCoordinates.Length ||
                    texturePolygon.D >= part.TextureCoordinates.Length)
                    return false;
            }
        }

        return true;
    }

    private static int SelectTextureBlockPart(
        IReadOnlyList<ParsedMeshPart> parts,
        TexturePolygonBlock block,
        IReadOnlyList<HashSet<uint>> usedPolygons)
    {
        var bestPartIndex = -1;
        var bestUnusedCount = -1;
        var bestValidCount = -1;
        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var used = usedPolygons[partIndex];
            var validCount = 0;
            var unusedCount = 0;
            foreach (var texturePolygon in block.Polygons)
            {
                if (texturePolygon.A >= part.Polygons.Length ||
                    texturePolygon.B >= part.TextureCoordinates.Length ||
                    texturePolygon.C >= part.TextureCoordinates.Length ||
                    texturePolygon.D >= part.TextureCoordinates.Length)
                    continue;

                validCount++;
                if (!used.Contains(texturePolygon.A))
                    unusedCount++;
            }

            if (unusedCount < bestUnusedCount ||
                (unusedCount == bestUnusedCount && validCount <= bestValidCount))
                continue;

            bestPartIndex = partIndex;
            bestUnusedCount = unusedCount;
            bestValidCount = validCount;
        }

        return bestUnusedCount > 0 || bestValidCount > 0 ? bestPartIndex : -1;
    }

    private static List<MeshSurface> BuildSequentialMesh(
        IReadOnlyList<ParsedMeshPart> parts,
        IReadOnlyList<TexturePolygon> texturePolygons,
        IReadOnlyList<TexturePolygonBlock> texturePolygonBlocks,
        IReadOnlyList<string> textureNames,
        Vector3 min,
        Vector3 center,
        int verticalAxis,
        int horizontalAxis0,
        int horizontalAxis1,
        float scale,
        List<VertexPositionNormalTexture> vertices,
        List<ushort> indices)
    {
        var surfaceRanges = BuildSurfaceRanges(texturePolygonBlocks, textureNames);
        var globalPolygonIndex = 0;

        foreach (var part in parts)
        {
            foreach (var polygon in part.Polygons)
            {
                if (vertices.Count + 3 > MaximumMeshVertices)
                    break;

                if (!IsValidPolygon(part, polygon))
                {
                    globalPolygonIndex++;
                    continue;
                }

                var texturePolygon = globalPolygonIndex < texturePolygons.Count
                    ? texturePolygons[globalPolygonIndex]
                    : default;

                AddCorner(part, polygon.A, texturePolygon.B, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, scale, vertices, indices);
                AddCorner(part, polygon.B, texturePolygon.C, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, scale, vertices, indices);
                AddCorner(part, polygon.C, texturePolygon.D, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, scale, vertices, indices);
                globalPolygonIndex++;
            }
        }

        return surfaceRanges;
    }

    private static void AddCorner(
        ParsedMeshPart part,
        uint positionIndex,
        uint textureIndex,
        Vector3 min,
        Vector3 center,
        int verticalAxis,
        int horizontalAxis0,
        int horizontalAxis1,
        float scale,
        List<VertexPositionNormalTexture> vertices,
        List<ushort> indices)
    {
        if (positionIndex >= part.Positions.Length)
            return;

        var source = part.Positions[positionIndex];
        var position = new Vector3(
            (Axis(source, horizontalAxis0) - Axis(center, horizontalAxis0)) * scale,
            (Axis(source, horizontalAxis1) - Axis(center, horizontalAxis1)) * scale,
            (Axis(source, verticalAxis) - Axis(min, verticalAxis)) * scale);

        var texCoord = textureIndex < part.TextureCoordinates.Length
            ? SanitizeTexCoord(part.TextureCoordinates[textureIndex])
            : Vector2.Zero;

        indices.Add(checked((ushort)vertices.Count));
        vertices.Add(new VertexPositionNormalTexture(position, Vector3.Zero, texCoord));
    }

    private static Vector2 SanitizeTexCoord(Vector2 texCoord) =>
        new(SanitizeTexCoordComponent(texCoord.X), SanitizeTexCoordComponent(texCoord.Y));

    private static float SanitizeTexCoordComponent(float value) =>
        float.IsFinite(value) ? value : 0.0f;

    private static bool IsValidPolygon(ParsedMeshPart part, GrannyPolygon polygon) =>
        polygon.A < part.Positions.Length &&
        polygon.B < part.Positions.Length &&
        polygon.C < part.Positions.Length;

    private static ItemDescriptor[] ReadItemDescriptors(ReadOnlySpan<byte> data, int listBase, int descriptorScanLength)
    {
        var descriptorStart = listBase + ItemListHeaderSize;
        var scanEnd = Math.Min(descriptorScanLength, data.Length);
        if (descriptorStart > scanEnd)
            return [];

        var descriptorCount = ReadUInt32(data, listBase);
        var availableCount = (scanEnd - descriptorStart) / DescriptorSize;
        if (descriptorCount == 0 || descriptorCount > availableCount)
            return [];

        var descriptors = new ItemDescriptor[descriptorCount];
        for (var i = 0; i < descriptors.Length; i++)
        {
            var descriptorOffset = descriptorStart + i * DescriptorSize;
            var relativeOffset = ReadUInt32(data, descriptorOffset + 4);
            var descendantCount = ReadUInt32(data, descriptorOffset + 8);
            descriptors[i] = new ItemDescriptor(
                ReadUInt32(data, descriptorOffset),
                relativeOffset,
                AddOffset(listBase, relativeOffset),
                descendantCount <= int.MaxValue ? (int)descendantCount : int.MaxValue,
                descriptorOffset);
        }

        return descriptors;
    }

    private static List<TexturePolygonBlock> ReadTexturePolygonBlocks(
        ReadOnlySpan<byte> data,
        int listBase,
        int descriptorScanLength,
        IReadOnlyList<ItemDescriptor> descriptors)
    {
        var blocks = new List<TexturePolygonBlock>();
        var seenOffsets = new HashSet<int>();
        var textureListIndex = FindImmediateChild(descriptors, -1, TextureListChunk);
        if (textureListIndex >= 0)
        {
            var textureListEnd = DescendantEndExclusive(descriptors, textureListIndex);
            for (var groupIndex = textureListIndex + 1; groupIndex < textureListEnd; groupIndex++)
            {
                var group = descriptors[groupIndex];
                if (group.Chunk != TexturePolygonGroupChunk ||
                    group.DataOffset < 0 ||
                    group.DataOffset + 8 > data.Length)
                    continue;

                var meshIndexValue = ReadUInt32(data, group.DataOffset);
                var textureIndexValue = ReadUInt32(data, group.DataOffset + 4);
                var meshIndex = meshIndexValue <= int.MaxValue ? (int)meshIndexValue : -1;
                var textureIndex = textureIndexValue <= int.MaxValue ? (int)textureIndexValue : -1;
                var groupEnd = DescendantEndExclusive(descriptors, groupIndex);
                for (var descriptorIndex = groupIndex + 1; descriptorIndex < groupEnd; descriptorIndex++)
                {
                    if (descriptors[descriptorIndex].Chunk != TexturePolygonChunk)
                        continue;

                    var block = ReadTexturePolygonBlock(
                        data,
                        listBase,
                        descriptorScanLength,
                        descriptors,
                        descriptorIndex,
                        meshIndex,
                        textureIndex);
                    if (block is not null && seenOffsets.Add(descriptors[descriptorIndex].DataOffset))
                        blocks.Add(block);
                }
            }
        }

        if (blocks.Count > 0)
            return blocks;

        for (var descriptorIndex = 0; descriptorIndex < descriptors.Count; descriptorIndex++)
        {
            if (descriptors[descriptorIndex].Chunk != TexturePolygonChunk)
                continue;

            var block = ReadTexturePolygonBlock(
                data,
                listBase,
                descriptorScanLength,
                descriptors,
                descriptorIndex,
                -1,
                -1);
            if (block is not null && seenOffsets.Add(descriptors[descriptorIndex].DataOffset))
                blocks.Add(block);
        }

        return blocks;
    }

    private static TexturePolygonBlock? ReadTexturePolygonBlock(
        ReadOnlySpan<byte> data,
        int listBase,
        int descriptorScanLength,
        IReadOnlyList<ItemDescriptor> descriptors,
        int descriptorIndex,
        int meshIndex,
        int textureIndex)
    {
        var descriptor = descriptors[descriptorIndex];
        var dataOffset = descriptor.DataOffset;
        if (dataOffset < 0 || dataOffset + 4 > data.Length)
            return null;

        var nextDataOffset = descriptorIndex + 1 < descriptors.Count
            ? AddOffset(listBase, descriptors[descriptorIndex + 1].RelativeOffset)
            : Math.Min(descriptorScanLength, data.Length);
        if (nextDataOffset <= dataOffset + 4)
            return null;

        var polygonCountValue = ReadUInt32(data, dataOffset);
        if (polygonCountValue == 0 || polygonCountValue > 100_000)
            return null;

        var polygonCount = (int)polygonCountValue;
        var dataByteLength = nextDataOffset - dataOffset - 4;
        if (dataByteLength % polygonCount != 0)
            return null;

        var entrySize = dataByteLength / polygonCount;
        if (entrySize is not 16 and not 28)
            return null;

        var entriesOffset = dataOffset + 4;
        var byteLength = checked(polygonCount * entrySize);
        if (entriesOffset + byteLength > data.Length)
            return null;

        var polygons = new TexturePolygon[polygonCount];
        for (var i = 0; i < polygons.Length; i++)
        {
            var offset = entriesOffset + i * entrySize;
            polygons[i] = entrySize == 28
                ? new TexturePolygon(
                    ReadUInt32(data, offset),
                    ReadUInt32(data, offset + 8),
                    ReadUInt32(data, offset + 16),
                    ReadUInt32(data, offset + 24))
                : new TexturePolygon(
                    ReadUInt32(data, offset),
                    ReadUInt32(data, offset + 4),
                    ReadUInt32(data, offset + 8),
                    ReadUInt32(data, offset + 12));
        }

        return new TexturePolygonBlock(polygons, meshIndex, textureIndex);
    }

    private static string[] ReadMaterialTextureNames(
        ReadOnlySpan<byte> data,
        int descriptorScanLength,
        IReadOnlyList<ItemDescriptor> descriptors)
    {
        var textureNames = ReadTextureNames(data, descriptorScanLength, descriptors);
        var materialTextureSlots = ReadMaterialTextureSlots(data, descriptors);
        if (textureNames.Length == 0 || materialTextureSlots.Length == 0)
            return textureNames;

        var materialTextureNames = new string[materialTextureSlots.Length];
        for (var materialIndex = 0; materialIndex < materialTextureSlots.Length; materialIndex++)
        {
            var textureIndex = materialTextureSlots[materialIndex] - 1;
            if ((uint)textureIndex < (uint)textureNames.Length)
                materialTextureNames[materialIndex] = textureNames[textureIndex];
        }

        return materialTextureNames;
    }

    private static int[] ReadMaterialTextureSlots(
        ReadOnlySpan<byte> data,
        IReadOnlyList<ItemDescriptor> descriptors)
    {
        var materialListIndex = FindImmediateChild(descriptors, -1, MaterialListChunk);
        if (materialListIndex < 0)
            return [];

        var slots = new List<int>();
        foreach (var materialIndex in EnumerateImmediateChildren(descriptors, materialListIndex))
        {
            if (descriptors[materialIndex].Chunk != MaterialChunk)
                continue;

            var textureSlot = -1;
            var end = DescendantEndExclusive(descriptors, materialIndex);
            for (var descriptorIndex = materialIndex + 1; descriptorIndex < end; descriptorIndex++)
            {
                var descriptor = descriptors[descriptorIndex];
                if (descriptor.Chunk != MaterialTextureSlotChunk ||
                    descriptor.DataOffset < 0 ||
                    descriptor.DataOffset + 8 > data.Length)
                    continue;

                var textureSlotValue = ReadUInt32(data, descriptor.DataOffset + 4);
                textureSlot = textureSlotValue <= int.MaxValue ? (int)textureSlotValue : -1;
                break;
            }

            slots.Add(textureSlot);
        }

        return slots.ToArray();
    }

    private static string[] ReadTextureNames(
        ReadOnlySpan<byte> data,
        int descriptorScanLength,
        IReadOnlyList<ItemDescriptor> descriptors)
    {
        var textEntries = ReadTextEntries(data, descriptorScanLength, descriptors);
        var objects = ReadObjects(data, descriptors);
        var textureInfosIndex = FindImmediateChild(descriptors, -1, TextureInfosChunk);
        var fileNameKey = FindStringIndex(textEntries, "__FileName");
        if (textureInfosIndex < 0 || fileNameKey < 0 || objects.Count == 0)
            return ExtractTextureNames(data[..Math.Min(descriptorScanLength, data.Length)]).ToArray();

        var names = new List<string>();
        foreach (var textureInfoIndex in EnumerateImmediateChildren(descriptors, textureInfosIndex))
        {
            if (descriptors[textureInfoIndex].Chunk != TextureInfoChunk)
                continue;

            var textureObjectId = 0u;
            var end = DescendantEndExclusive(descriptors, textureInfoIndex);
            for (var descriptorIndex = textureInfoIndex + 1; descriptorIndex < end; descriptorIndex++)
            {
                var descriptor = descriptors[descriptorIndex];
                if (descriptor.Chunk == MeshIdChunk &&
                    descriptor.DataOffset >= 0 &&
                    descriptor.DataOffset + 4 <= data.Length)
                    textureObjectId = ReadUInt32(data, descriptor.DataOffset);
            }

            names.Add(ResolveTextureName(textureObjectId, (uint)fileNameKey, textEntries, objects));
        }

        return names.Count > 0
            ? names.ToArray()
            : ExtractTextureNames(data[..Math.Min(descriptorScanLength, data.Length)]).ToArray();
    }

    private static string[] ReadTextEntries(
        ReadOnlySpan<byte> data,
        int descriptorScanLength,
        IReadOnlyList<ItemDescriptor> descriptors)
    {
        var textIndex = FindImmediateChild(descriptors, -1, TextChunk);
        if (textIndex < 0)
            return [];

        var dataOffset = descriptors[textIndex].DataOffset;
        var scanEnd = Math.Min(descriptorScanLength, data.Length);
        if (dataOffset < 0 || dataOffset + 8 > scanEnd)
            return [];

        var countValue = ReadUInt32(data, dataOffset);
        if (countValue > 100_000)
            return [];

        var entries = new List<string>((int)countValue);
        var position = dataOffset + 8;
        for (var i = 0; i < countValue; i++)
        {
            if (position >= scanEnd)
                return [];

            var length = data[position..scanEnd].IndexOf((byte)0);
            if (length < 0)
                return [];

            entries.Add(Encoding.Latin1.GetString(data.Slice(position, length)));
            position += length + 1;
        }

        return entries.ToArray();
    }

    private static List<Dictionary<uint, uint>> ReadObjects(
        ReadOnlySpan<byte> data,
        IReadOnlyList<ItemDescriptor> descriptors)
    {
        var objectListIndex = FindImmediateChild(descriptors, -1, ObjectListChunk);
        var objects = new List<Dictionary<uint, uint>>();
        if (objectListIndex < 0)
            return objects;

        foreach (var objectIndex in EnumerateImmediateChildren(descriptors, objectListIndex))
        {
            if (descriptors[objectIndex].Chunk != ObjectDataChunk)
                continue;

            var values = new Dictionary<uint, uint>();
            uint? key = null;
            var end = DescendantEndExclusive(descriptors, objectIndex);
            for (var descriptorIndex = objectIndex + 1; descriptorIndex < end; descriptorIndex++)
            {
                var descriptor = descriptors[descriptorIndex];
                if (descriptor.DataOffset < 0)
                    continue;

                if (descriptor.Chunk == ObjectKeyChunk && descriptor.DataOffset + 4 <= data.Length)
                {
                    key = ReadUInt32(data, descriptor.DataOffset);
                }
                else if (descriptor.Chunk == ObjectValueChunk &&
                         key is { } objectKey &&
                         descriptor.DataOffset + 8 <= data.Length)
                {
                    values[objectKey] = ReadUInt32(data, descriptor.DataOffset + 4);
                }
            }

            objects.Add(values);
        }

        return objects;
    }

    private static string ResolveTextureName(
        uint textureObjectId,
        uint fileNameKey,
        IReadOnlyList<string> textEntries,
        IReadOnlyList<Dictionary<uint, uint>> objects)
    {
        if (textureObjectId == 0 || textureObjectId > objects.Count)
            return string.Empty;

        var textureObject = objects[(int)textureObjectId - 1];
        if (!textureObject.TryGetValue(fileNameKey, out var textId) || textId >= textEntries.Count)
            return string.Empty;

        var raw = textEntries[(int)textId].Replace('\\', '/');
        var slash = raw.LastIndexOf('/');
        return slash >= 0 ? raw[(slash + 1)..] : raw;
    }

    private static int FindStringIndex(IReadOnlyList<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static int FindImmediateChild(
        IReadOnlyList<ItemDescriptor> descriptors,
        int parentIndex,
        uint chunk)
    {
        foreach (var descriptorIndex in EnumerateImmediateChildren(descriptors, parentIndex))
        {
            if (descriptors[descriptorIndex].Chunk == chunk)
                return descriptorIndex;
        }

        return -1;
    }

    private static IEnumerable<int> EnumerateImmediateChildren(
        IReadOnlyList<ItemDescriptor> descriptors,
        int parentIndex)
    {
        var descriptorIndex = parentIndex < 0 ? 0 : parentIndex + 1;
        var end = parentIndex < 0
            ? descriptors.Count
            : DescendantEndExclusive(descriptors, parentIndex);
        while (descriptorIndex < end)
        {
            yield return descriptorIndex;
            var next = (long)descriptorIndex + 1 + descriptors[descriptorIndex].DescendantCount;
            if (next <= descriptorIndex || next > end)
                yield break;

            descriptorIndex = (int)next;
        }
    }

    private static int DescendantEndExclusive(IReadOnlyList<ItemDescriptor> descriptors, int descriptorIndex)
    {
        var end = (long)descriptorIndex + 1 + descriptors[descriptorIndex].DescendantCount;
        return (int)Math.Min(descriptors.Count, end);
    }

    private static List<MeshSurface> BuildSurfaceRanges(IReadOnlyList<TexturePolygonBlock> blocks, IReadOnlyList<string> textureNames)
    {
        var surfaces = new List<MeshSurface>(blocks.Count);
        var polygonStart = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            var polygonCount = blocks[i].Polygons.Length;
            if (polygonCount > 0)
                surfaces.Add(new MeshSurface(
                    polygonStart * 3,
                    polygonCount * 3,
                    TextureNameForBlock(blocks[i], i, textureNames)));

            polygonStart += polygonCount;
        }

        return surfaces;
    }

    private static MeshSurface[] ClipSurfaceRanges(IReadOnlyList<MeshSurface> surfaces, int indexCount)
    {
        if (surfaces.Count == 0 || indexCount <= 0)
            return [];

        var clipped = new List<MeshSurface>(surfaces.Count);
        var nextIndex = 0;
        foreach (var surface in surfaces)
        {
            if (surface.IndexStart >= indexCount)
                continue;

            if (surface.IndexStart > nextIndex)
                clipped.Add(new MeshSurface(nextIndex, surface.IndexStart - nextIndex, null));

            var count = Math.Min(surface.IndexCount, indexCount - surface.IndexStart);
            if (count > 0)
            {
                clipped.Add(surface with { IndexCount = count });
                nextIndex = surface.IndexStart + count;
            }
        }

        if (nextIndex < indexCount)
            clipped.Add(new MeshSurface(nextIndex, indexCount - nextIndex, null));

        return clipped.ToArray();
    }

    private static List<string> ExtractTextureNames(ReadOnlySpan<byte> data)
    {
        var names = new List<string>();
        for (var offset = 0; offset < data.Length;)
        {
            var extensionOffset = IndexOfTextureExtension(data[offset..]);
            if (extensionOffset < 0)
                break;

            extensionOffset += offset;
            var start = extensionOffset;
            while (start > 0 && IsTexturePathByte(data[start - 1]))
                start--;

            var end = extensionOffset + 4;
            if (end > start)
            {
                var raw = Encoding.Latin1.GetString(data[start..end]).Replace('\\', '/');
                var slash = raw.LastIndexOf('/');
                var name = slash >= 0 ? raw[(slash + 1)..] : raw;
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
            }

            offset = end;
        }

        return names;
    }

    private static int IndexOfTextureExtension(ReadOnlySpan<byte> data)
    {
        for (var i = 0; i + 4 <= data.Length; i++)
        {
            if (data[i] != (byte)'.')
                continue;

            if (ToLowerAscii(data[i + 1]) == (byte)'t' &&
                ToLowerAscii(data[i + 2]) == (byte)'g' &&
                ToLowerAscii(data[i + 3]) == (byte)'a')
                return i;
        }

        return -1;
    }

    private static bool IsTexturePathByte(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' ||
        value is >= (byte)'a' and <= (byte)'z' ||
        value is >= (byte)'0' and <= (byte)'9' ||
        value is (byte)'_' or (byte)'-' or (byte)'.' or (byte)'/' or (byte)'\\' or (byte)':';

    private static byte ToLowerAscii(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value;

    private static Bounds CalculateBounds(IEnumerable<ParsedMeshPart> parts)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var part in parts)
        {
            foreach (var position in part.Positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }
        }

        return new Bounds(min, max);
    }

    private static List<int> FindGrannySliceStarts(ReadOnlySpan<byte> data)
    {
        var starts = new List<int>();
        for (var offset = HeaderSize; offset + 8 <= data.Length; offset++)
        {
            if (ReadUInt32(data, offset) != MainChunk || !LooksLikeGrannyMainChunk(data, offset))
                continue;

            var start = offset - HeaderSize;
            if (starts.Count == 0 || starts[^1] != start)
                starts.Add(start);
        }

        return starts;
    }

    private static bool LooksLikeGrannyMainChunk(ReadOnlySpan<byte> data, int mainChunkOffset)
    {
        var childCount = ReadUInt32(data, mainChunkOffset + 4);
        if (childCount == 0 || childCount > 16)
            return false;

        var descriptorOffset = mainChunkOffset + 4 + 4 + 24;
        var hasObject = false;
        for (var child = 0; child < childCount; child++)
        {
            if (descriptorOffset + 20 > data.Length)
                return false;

            var chunk = ReadUInt32(data, descriptorOffset);
            if (chunk is not FinalChunk and not CopyrightChunk and not ObjectChunk)
                return false;

            hasObject |= chunk == ObjectChunk;
            descriptorOffset += 20;
        }

        return hasObject;
    }

    private static IEnumerable<(int Start, int End)> EnumerateSlices(IReadOnlyList<int> starts, int length)
    {
        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i];
            var end = i + 1 < starts.Count ? starts[i + 1] : length;
            if (start >= 0 && end > start)
                yield return (start, end);
        }
    }

    private static bool OffsetsAreValid(int length, params int[] offsets)
    {
        for (var i = 0; i < offsets.Length; i++)
        {
            if (offsets[i] < 0 || offsets[i] >= length)
                return false;

            if (i > 0 && offsets[i] <= offsets[i - 1])
                return false;
        }

        return true;
    }

    private static int AddOffset(int baseOffset, uint relativeOffset)
    {
        var absolute = baseOffset + (long)relativeOffset;
        return absolute is >= 0 and <= int.MaxValue ? (int)absolute : -1;
    }

    private static int VerticalAxis(Vector3 span) =>
        span.X >= span.Y && span.X >= span.Z
            ? 0
            : span.Y >= span.Z ? 1 : 2;

    private static float Axis(Vector3 value, int axis) =>
        axis switch
        {
            0 => value.X,
            1 => value.Y,
            _ => value.Z
        };

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));

    private static float ReadSingle(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.ToSingle(data.Slice(offset, 4));

    private sealed record ParsedMeshSlice(
        ParsedMeshPart[] Parts,
        TexturePolygon[] TexturePolygons,
        TexturePolygonBlock[] TexturePolygonBlocks,
        string[] TextureNames,
        GrannySkeleton? Skeleton,
        IReadOnlySet<string>? HiddenTextureNames);

    private readonly record struct ParsedMeshPart(
        int PointOffset,
        int PolygonOffset,
        Vector3[] Positions,
        Vector2[] TextureCoordinates,
        GrannyPolygon[] Polygons,
        VertexWeight[][] Weights);

    private readonly record struct VertexWeight(uint BoneTieIndex, float Weight);

    private sealed record GrannySkeleton(
        GrannyBone[] Bones,
        uint[] BoneTieBones,
        IReadOnlyDictionary<string, int> BonesByName);

    private readonly record struct GrannyBone(string Name, Matrix4x4 RestWorld);

    private readonly record struct GrannyPolygon(uint A, uint B, uint C);

    private readonly record struct TexturePolygon(uint A, uint B, uint C, uint D);

    private sealed record TexturePolygonBlock(TexturePolygon[] Polygons, int MeshIndex, int TextureIndex);

    private readonly record struct ItemDescriptor(
        uint Chunk,
        uint RelativeOffset,
        int DataOffset,
        int DescendantCount,
        int DescriptorOffset);

    private readonly record struct MeshKey(int PointOffset, int PolygonOffset);

    private readonly record struct Bounds(Vector3 Min, Vector3 Max);
}

public static class GrnStaticMeshExtractor
{
    private const int MinimumIndexCount = 90;
    private const int MinimumUniqueIndices = 20;
    private const int MaximumVertexCount = 20000;
    private const int PositionStride = 12;
    private const int PositionSearchBackBytes = 64 * 1024;
    private const int MaximumMeshParts = 8;
    private const float TargetHeight = 145.0f;

    public static Mesh? TryExtract(ReadOnlySpan<byte> data)
    {
        var parts = new List<MeshPart>();
        foreach (var run in FindIndexRuns(data).OrderByDescending(static r => r.IndexCount))
        {
            if (parts.Count >= MaximumMeshParts)
                break;

            if (OverlapsExistingPart(parts, run.Start, run.ByteLength))
                continue;

            var positions = FindPositionBlock(data, run);
            if (positions is null)
                continue;

            parts.Add(new MeshPart(run, positions.Value));
        }

        if (parts.Count == 0)
            return null;

        return BuildMesh(parts);
    }

    private static List<IndexRun> FindIndexRuns(ReadOnlySpan<byte> data)
    {
        var runs = new List<IndexRun>();
        for (var offset = 0; offset + 4 <= data.Length;)
        {
            if ((offset & 3) != 0)
            {
                offset++;
                continue;
            }

            var start = offset;
            var values = new List<uint>();
            while (offset + 4 <= data.Length)
            {
                var value = BitConverter.ToUInt32(data.Slice(offset, 4));
                if (value >= MaximumVertexCount)
                    break;

                values.Add(value);
                offset += 4;
            }

            if (values.Count >= MinimumIndexCount)
            {
                var max = values.Max();
                var unique = values.Distinct().Count();
                if (max >= MinimumUniqueIndices && unique >= MinimumUniqueIndices && LooksLikeTriangleStream(values))
                    runs.Add(new IndexRun(start, values.ToArray(), checked((int)max + 1), unique));
            }

            offset = Math.Max(offset + 4, start + 4);
        }

        return runs;
    }

    private static bool LooksLikeTriangleStream(IReadOnlyList<uint> values)
    {
        var nonSequential = 0;
        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] != values[i - 1] + 1)
                nonSequential++;
        }

        return nonSequential > values.Count * 0.6;
    }

    private static PositionBlock? FindPositionBlock(ReadOnlySpan<byte> data, IndexRun run)
    {
        var byteLength = run.VertexCount * PositionStride;
        if (byteLength <= 0 || run.Start < byteLength)
            return null;

        var bestScore = float.MinValue;
        PositionBlock? best = null;
        var searchStart = Math.Max(0, run.Start - byteLength - PositionSearchBackBytes);
        var searchEnd = run.Start - byteLength;

        for (var start = searchEnd; start >= searchStart; start -= 4)
        {
            if (!TryReadPositionBlock(data, start, run.VertexCount, out var block))
                continue;

            var score = block.Score + run.IndexCount * 0.01f;
            if (score <= bestScore)
                continue;

            bestScore = score;
            best = block;
        }

        return best;
    }

    private static bool TryReadPositionBlock(ReadOnlySpan<byte> data, int start, int vertexCount, out PositionBlock block)
    {
        block = default;
        if (start < 0 || start + vertexCount * PositionStride > data.Length)
            return false;

        var positions = new Vector3[vertexCount];
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var nonZero = 0;

        for (var i = 0; i < vertexCount; i++)
        {
            var offset = start + i * PositionStride;
            var position = new Vector3(
                BitConverter.ToSingle(data.Slice(offset + 0, 4)),
                BitConverter.ToSingle(data.Slice(offset + 4, 4)),
                BitConverter.ToSingle(data.Slice(offset + 8, 4)));

            if (!IsPlausiblePosition(position))
                return false;

            if (MathF.Abs(position.X) > 0.1f || MathF.Abs(position.Y) > 0.1f || MathF.Abs(position.Z) > 0.1f)
                nonZero++;

            positions[i] = position;
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        if (nonZero < vertexCount * 0.70f)
            return false;

        var span = max - min;
        var wideAxes = 0;
        if (span.X > 5.0f) wideAxes++;
        if (span.Y > 5.0f) wideAxes++;
        if (span.Z > 5.0f) wideAxes++;

        if (span.Length() < 20.0f || span.Length() > 600.0f || wideAxes < 2)
            return false;

        block = new PositionBlock(start, positions, min, max, span.X + span.Y + span.Z);
        return true;
    }

    private static bool IsPlausiblePosition(Vector3 position) =>
        float.IsFinite(position.X) &&
        float.IsFinite(position.Y) &&
        float.IsFinite(position.Z) &&
        position.X is > -2000.0f and < 2000.0f &&
        position.Y is > -2000.0f and < 2000.0f &&
        position.Z is > -2000.0f and < 2000.0f;

    private static Mesh BuildMesh(IReadOnlyList<MeshPart> parts)
    {
        var rawPositions = new List<Vector3>();
        foreach (var part in parts)
            rawPositions.AddRange(part.Positions.Positions);

        var globalMin = new Vector3(float.MaxValue);
        var globalMax = new Vector3(float.MinValue);
        foreach (var position in rawPositions)
        {
            globalMin = Vector3.Min(globalMin, position);
            globalMax = Vector3.Max(globalMax, position);
        }

        var span = globalMax - globalMin;
        var verticalAxis = span.X >= span.Y && span.X >= span.Z
            ? 0
            : span.Y >= span.Z ? 1 : 2;
        var scale = TargetHeight / Math.Max(1.0f, Axis(span, verticalAxis));

        var rawCenter = (globalMin + globalMax) * 0.5f;
        var bottom = Axis(globalMin, verticalAxis);
        var vertices = new List<VertexPositionNormalTexture>();
        var indices = new List<ushort>();

        foreach (var part in parts)
        {
            if (vertices.Count + part.Positions.Positions.Length > ushort.MaxValue)
                break;

            var vertexBase = vertices.Count;
            foreach (var position in part.Positions.Positions)
                vertices.Add(new VertexPositionNormalTexture(ProjectPosition(position, rawCenter, bottom, verticalAxis, scale), Vector3.Zero, Vector2.Zero));

            var usableIndexCount = part.Indices.Values.Length - part.Indices.Values.Length % 3;
            for (var i = 0; i < usableIndexCount; i++)
            {
                var sourceIndex = part.Indices.Values[i];
                if (sourceIndex >= part.Positions.Positions.Length)
                    continue;

                indices.Add(checked((ushort)(vertexBase + (int)sourceIndex)));
            }
        }

        var vertexArray = vertices.ToArray();
        var indexArray = indices.ToArray();
        RecalculateNormals(vertexArray, indexArray);
        return new Mesh(vertexArray, indexArray);
    }

    private static Vector3 ProjectPosition(Vector3 source, Vector3 center, float bottom, int verticalAxis, float scale)
    {
        var a = Axis(source, verticalAxis);
        var h0 = (verticalAxis + 1) % 3;
        var h1 = (verticalAxis + 2) % 3;

        return new Vector3(
            (Axis(source, h0) - Axis(center, h0)) * scale,
            (Axis(source, h1) - Axis(center, h1)) * scale,
            (a - bottom) * scale);
    }

    private static float Axis(Vector3 value, int axis) =>
        axis switch
        {
            0 => value.X,
            1 => value.Y,
            _ => value.Z
        };

    internal static void RecalculateNormals(VertexPositionNormalTexture[] vertices, ushort[] indices)
    {
        var normals = new Vector3[vertices.Length];
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var i0 = indices[i + 0];
            var i1 = indices[i + 1];
            var i2 = indices[i + 2];
            var p0 = vertices[i0].Position;
            var p1 = vertices[i1].Position;
            var p2 = vertices[i2].Position;
            var normal = Vector3.Cross(p1 - p0, p2 - p0);
            if (normal.LengthSquared() <= 0.000001f)
                continue;

            normals[i0] += normal;
            normals[i1] += normal;
            normals[i2] += normal;
        }

        for (var i = 0; i < vertices.Length; i++)
        {
            var normal = normals[i].LengthSquared() > 0.000001f
                ? Vector3.Normalize(normals[i])
                : Vector3.UnitZ;
            vertices[i] = vertices[i] with { Normal = normal };
        }
    }

    private static bool OverlapsExistingPart(IEnumerable<MeshPart> parts, int start, int length)
    {
        var end = start + length;
        foreach (var part in parts)
        {
            if (start < part.Indices.Start + part.Indices.ByteLength && end > part.Indices.Start)
                return true;

            if (start < part.Positions.Start + part.Positions.ByteLength && end > part.Positions.Start)
                return true;
        }

        return false;
    }

    private readonly record struct IndexRun(int Start, uint[] Values, int VertexCount, int UniqueCount)
    {
        public int IndexCount => Values.Length;
        public int ByteLength => Values.Length * 4;
    }

    private readonly record struct PositionBlock(int Start, Vector3[] Positions, Vector3 Min, Vector3 Max, float Score)
    {
        public int ByteLength => Positions.Length * PositionStride;
    }

    private readonly record struct MeshPart(IndexRun Indices, PositionBlock Positions);
}

public static class MeshFactory
{
    public static Mesh CreateHumanoidProxyMesh()
    {
        var vertices = new VertexPositionNormalTexture[]
        {
            new(new(-18, 0, 0), Vector3.UnitZ, new(0, 1)),
            new(new(18, 0, 0), Vector3.UnitZ, new(1, 1)),
            new(new(18, 0, 70), Vector3.UnitZ, new(1, 0)),
            new(new(-18, 0, 70), Vector3.UnitZ, new(0, 0)),

            new(new(-28, 0, 70), Vector3.UnitZ, new(0, 1)),
            new(new(28, 0, 70), Vector3.UnitZ, new(1, 1)),
            new(new(20, 0, 115), Vector3.UnitZ, new(1, 0)),
            new(new(-20, 0, 115), Vector3.UnitZ, new(0, 0)),

            new(new(-14, 0, 115), Vector3.UnitZ, new(0, 1)),
            new(new(14, 0, 115), Vector3.UnitZ, new(1, 1)),
            new(new(14, 0, 145), Vector3.UnitZ, new(1, 0)),
            new(new(-14, 0, 145), Vector3.UnitZ, new(0, 0)),
        };

        ushort[] indices =
        [
            0, 1, 2, 0, 2, 3,
            4, 5, 6, 4, 6, 7,
            8, 9, 10, 8, 10, 11
        ];

        return new Mesh(vertices, indices);
    }

    public static Mesh CreateShieldTowerProxyMesh()
    {
        var vertices = new VertexPositionNormalTexture[]
        {
            new(new(-35, -35, 0), Vector3.UnitZ, new(0, 1)),
            new(new(35, -35, 0), Vector3.UnitZ, new(1, 1)),
            new(new(35, 35, 0), Vector3.UnitZ, new(1, 0)),
            new(new(-35, 35, 0), Vector3.UnitZ, new(0, 0)),
            new(new(-28, -28, 120), Vector3.UnitZ, new(0, 1)),
            new(new(28, -28, 120), Vector3.UnitZ, new(1, 1)),
            new(new(28, 28, 120), Vector3.UnitZ, new(1, 0)),
            new(new(-28, 28, 120), Vector3.UnitZ, new(0, 0)),
            new(new(0, 0, 170), Vector3.UnitZ, new(0.5f, 0))
        };

        ushort[] indices =
        [
            0, 1, 2, 0, 2, 3,
            0, 4, 5, 0, 5, 1,
            1, 5, 6, 1, 6, 2,
            2, 6, 7, 2, 7, 3,
            3, 7, 4, 3, 4, 0,
            4, 8, 5, 5, 8, 6, 6, 8, 7, 7, 8, 4
        ];

        return new Mesh(vertices, indices);
    }
}
