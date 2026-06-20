using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Sacred.Granny;

public static partial class Granny1MeshExtractor
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
    private const uint MeshListChunk = 0xCA5E0602;
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
    private const uint FormSectionChunk = 0xCA5E0C01;
    private const uint BoneObjectPointerChunk = 0xCA5E0C02;
    private const uint FormMeshChunk = 0xCA5E0C03;
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

    public static Mesh? TryExtract(
        ReadOnlySpan<byte> data,
        GrnMeshExtractionMode extractionMode = GrnMeshExtractionMode.PrimarySlice)
    {
        return Extract(data, extractionMode).Mesh;
    }

    public static GrnExtractionResult Extract(
        ReadOnlySpan<byte> data,
        GrnMeshExtractionMode extractionMode = GrnMeshExtractionMode.PrimarySlice,
        Vector3? modelScale = null)
    {
        var slices = ExtractSlices(data);

        var scale = SanitizeModelScale(modelScale ?? Vector3.One);
        if (scale != Vector3.One)
            slices = slices.Select(slice => ApplyModelScale(slice, scale)).ToList();

        if (slices.Count == 0)
            return new GrnExtractionResult(null, new GrnModelDiagnostics([], null, null));

        IReadOnlyList<ParsedMeshSlice> renderedSlices = extractionMode == GrnMeshExtractionMode.CompositeSlices
            ? slices
            : [SelectPrimarySlice(slices)];
        var mesh = BuildMesh(renderedSlices).Mesh;
        return new GrnExtractionResult(mesh, CreateDiagnostics(slices, renderedSlices));
    }

    private static GrnModelDiagnostics CreateDiagnostics(
        IReadOnlyList<ParsedMeshSlice> slices,
        IReadOnlyList<ParsedMeshSlice> renderedSlices)
    {
        var renderedParts = renderedSlices.SelectMany(static slice => slice.Parts).ToArray();
        var bounds = CalculateBounds(renderedParts);
        var verticalAxis = VerticalAxis(bounds.Max - bounds.Min);
        var horizontalAxis0 = (verticalAxis + 1) % 3;
        var horizontalAxis1 = (verticalAxis + 2) % 3;
        var center = (bounds.Min + bounds.Max) * 0.5f;
        const float scale = 1.0f;

        var projectedWholeModelPositions = slices
            .SelectMany(static slice => slice.Parts)
            .SelectMany(static part => part.Positions)
            .Select(position => ProjectPosition(
                position,
                bounds.Min,
                center,
                verticalAxis,
                horizontalAxis0,
                horizontalAxis1,
                scale))
            .ToArray();
        var wholeModelBounds = projectedWholeModelPositions.Length == 0
            ? (GrnBoundsDiagnostics?)null
            : new GrnBoundsDiagnostics(
                projectedWholeModelPositions.Aggregate(Vector3.Min),
                projectedWholeModelPositions.Aggregate(Vector3.Max));

        var sliceDiagnostics = slices.Select((slice, sliceIndex) =>
            new GrnSliceDiagnostics(
                sliceIndex,
                slice.Parts.Select((part, partIndex) => new GrnMeshPartDiagnostics(
                    partIndex,
                    part.Positions.Length,
                    part.Polygons.Length,
                    part.TextureCoordinates.Length,
                    part.Weights.Count(static weights => weights.Length > 0),
                    part.Weights.Sum(static weights => weights.Length))).ToArray(),
                slice.TextureNames,
                slice.TexturePolygons.Length,
                slice.TexturePolygonBlocks.Length,
                slice.Skeleton?.Bones.Select((bone, boneIndex) =>
                    new GrnBoneDiagnostics(
                        boneIndex,
                        bone.Name,
                        bone.ParentIndex,
                        ProjectPosition(
                            bone.RestWorld.Translation,
                            bounds.Min,
                            center,
                            verticalAxis,
                            horizontalAxis0,
                            horizontalAxis1,
                            scale))).ToArray() ?? [],
                slice.Skeleton?.BoneTieBones.Length ?? 0)).ToArray();
        var bonePositions = sliceDiagnostics
            .SelectMany(static slice => slice.Bones)
            .Select(static bone => bone.Position)
            .ToArray();
        var skeletonBounds = bonePositions.Length == 0
            ? (GrnBoundsDiagnostics?)null
            : new GrnBoundsDiagnostics(
                bonePositions.Aggregate(Vector3.Min),
                bonePositions.Aggregate(Vector3.Max));

        return new GrnModelDiagnostics(sliceDiagnostics, wholeModelBounds, skeletonBounds);
    }

    private static Vector3 ProjectPosition(
        Vector3 source,
        Vector3 min,
        Vector3 center,
        int verticalAxis,
        int horizontalAxis0,
        int horizontalAxis1,
        float scale) =>
        new(
            (Axis(source, horizontalAxis0) - Axis(center, horizontalAxis0)) * scale,
            (Axis(source, horizontalAxis1) - Axis(center, horizontalAxis1)) * scale,
            (Axis(source, verticalAxis) - Axis(min, verticalAxis)) * scale);

    public static Mesh? TryExtractCharacter(
        ReadOnlySpan<byte> baseData,
        IReadOnlyList<GrnCharacterAttachment> attachments,
        Vector3? baseModelScale = null) =>
        ExtractCharacter(baseData, attachments, baseModelScale).Mesh;

    public static GrnCharacterExtractionResult ExtractCharacter(
        ReadOnlySpan<byte> baseData,
        IReadOnlyList<GrnCharacterAttachment> attachments,
        Vector3? baseModelScale = null)
    {
        var baseSlices = ExtractSlices(baseData);
        if (baseSlices.Count == 0)
            return new GrnCharacterExtractionResult(
                null,
                null,
                new GrnModelDiagnostics([], null, null));

        var baseSlice = ApplyModelScale(
            SelectPrimarySlice(baseSlices),
            SanitizeModelScale(baseModelScale ?? Vector3.One));
        if (baseSlice.Skeleton is null)
            return new GrnCharacterExtractionResult(
                BuildMesh([baseSlice]).Mesh,
                null,
                CreateDiagnostics([baseSlice], [baseSlice]));

        baseSlice = BindSliceToSkeleton(baseSlice, baseSlice.Skeleton);

        var slices = new List<ParsedMeshSlice> { baseSlice };
        foreach (var attachmentSpec in attachments)
        {
            var attachmentSlices = ExtractSlices(attachmentSpec.Bytes);
            if (attachmentSlices.Count == 0)
                continue;

            var attachmentSlice = ApplyModelScale(
                SelectPrimarySlice(attachmentSlices),
                SanitizeModelScale(attachmentSpec.Scale));
            var attachment = string.IsNullOrWhiteSpace(attachmentSpec.RigidAttachBoneName)
                ? RetargetSlice(attachmentSlice, baseSlice.Skeleton)
                : AttachRigidSliceToBone(
                    attachmentSlice,
                    baseSlice.Skeleton,
                    attachmentSpec.RigidAttachBoneName,
                    attachmentSpec.SourceAttachBoneName);
            if (attachment is not null)
                slices.Add(attachment);
        }

        var result = BuildMesh(slices, baseSlice.Parts, baseSlice.Skeleton);
        return new GrnCharacterExtractionResult(
            result.Mesh,
            result.Skin,
            CreateDiagnostics(slices, [baseSlice]));
    }

    private static ParsedMeshSlice BindSliceToSkeleton(
        ParsedMeshSlice slice,
        GrannySkeleton targetSkeleton)
    {
        var parts = new ParsedMeshPart[slice.Parts.Length];
        for (var partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            var part = slice.Parts[partIndex];
            IReadOnlyList<uint> boneTieBones = part.BoneTieBones.Length > 0
                ? part.BoneTieBones
                : targetSkeleton.BoneTieBones;
            var targetBoneIndices = new int[boneTieBones.Count];
            Array.Fill(targetBoneIndices, -1);
            for (var tieIndex = 0; tieIndex < boneTieBones.Count; tieIndex++)
            {
                if (boneTieBones[tieIndex] < targetSkeleton.Bones.Length)
                    targetBoneIndices[tieIndex] = checked((int)boneTieBones[tieIndex]);
            }

            parts[partIndex] = part with { TargetBoneIndices = targetBoneIndices };
        }

        return slice with { Parts = parts };
    }

    private static ParsedMeshSlice ApplyModelScale(ParsedMeshSlice slice, Vector3 scale)
    {
        if (scale == Vector3.One)
            return slice;

        var parts = new ParsedMeshPart[slice.Parts.Length];
        for (var partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            var part = slice.Parts[partIndex];
            var positions = new Vector3[part.Positions.Length];
            for (var positionIndex = 0; positionIndex < positions.Length; positionIndex++)
                positions[positionIndex] = part.Positions[positionIndex] * scale;
            var normals = new Vector3[part.Normals.Length];
            var inverseScale = new Vector3(1.0f / scale.X, 1.0f / scale.Y, 1.0f / scale.Z);
            for (var normalIndex = 0; normalIndex < normals.Length; normalIndex++)
                normals[normalIndex] = NormalizeOrZero(part.Normals[normalIndex] * inverseScale);
            parts[partIndex] = part with { Positions = positions, Normals = normals };
        }

        return slice with
        {
            Parts = parts,
            Skeleton = slice.Skeleton is null ? null : ApplyModelScale(slice.Skeleton, scale)
        };
    }

    private static GrannySkeleton ApplyModelScale(GrannySkeleton skeleton, Vector3 scale)
    {
        var localTransforms = new Matrix4x4[skeleton.Bones.Length];
        var worldTransforms = new Matrix4x4[skeleton.Bones.Length];
        var parents = new int[skeleton.Bones.Length];
        for (var boneIndex = 0; boneIndex < skeleton.Bones.Length; boneIndex++)
        {
            var bone = skeleton.Bones[boneIndex];
            parents[boneIndex] = bone.ParentIndex;
            localTransforms[boneIndex] = CreateBoneTransform(
                bone.RestRotation,
                bone.RestTranslation * scale,
                bone.RestScaleShear);
        }

        var states = new byte[skeleton.Bones.Length];
        for (var boneIndex = 0; boneIndex < skeleton.Bones.Length; boneIndex++)
        {
            if (!TryComputeBoneWorldTransform(boneIndex, parents, localTransforms, worldTransforms, states))
                return skeleton;
        }

        var bones = new GrannyBone[skeleton.Bones.Length];
        for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            var bone = skeleton.Bones[boneIndex];
            bones[boneIndex] = bone with
            {
                RestTranslation = bone.RestTranslation * scale,
                RestLocal = localTransforms[boneIndex],
                RestWorld = worldTransforms[boneIndex]
            };
        }

        return CreateSkeleton(bones, skeleton.BoneTieBones);
    }

    private static Vector3 SanitizeModelScale(Vector3 scale) =>
        IsFinite(scale) &&
        MathF.Abs(scale.X) > 0.000001f &&
        MathF.Abs(scale.Y) > 0.000001f &&
        MathF.Abs(scale.Z) > 0.000001f
            ? scale
            : Vector3.One;

    private static ParsedMeshSlice? AttachRigidSliceToBone(
        ParsedMeshSlice slice,
        GrannySkeleton? targetSkeleton,
        string targetBoneName,
        string? sourceBoneName)
    {
        if (targetSkeleton is null ||
            !targetSkeleton.BonesByName.TryGetValue(targetBoneName, out var targetBoneIndex))
            return null;

        if (!GrnRigidTransform.TryCreate(targetSkeleton.Bones[targetBoneIndex].RestWorld, out var transform))
            return null;

        if (!string.IsNullOrWhiteSpace(sourceBoneName))
        {
            if (slice.Skeleton is null ||
                !slice.Skeleton.BonesByName.TryGetValue(sourceBoneName, out var sourceBoneIndex) ||
                !GrnRigidTransform.TryCreate(
                    slice.Skeleton.Bones[sourceBoneIndex].RestWorld,
                    out var sourceBoneTransform) ||
                !Matrix4x4.Invert(sourceBoneTransform, out var inverseSourceBoneTransform))
            {
                return null;
            }

            transform = inverseSourceBoneTransform * transform;
        }

        var parts = new ParsedMeshPart[slice.Parts.Length];
        for (var partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            var part = slice.Parts[partIndex];
            var positions = new Vector3[part.Positions.Length];
            for (var vertexIndex = 0; vertexIndex < positions.Length; vertexIndex++)
                positions[vertexIndex] = Vector3.Transform(part.Positions[vertexIndex], transform);
            var normals = new Vector3[part.Normals.Length];
            for (var normalIndex = 0; normalIndex < normals.Length; normalIndex++)
                normals[normalIndex] = NormalizeOrZero(Vector3.TransformNormal(part.Normals[normalIndex], transform));

            parts[partIndex] = part with
            {
                Positions = positions,
                Normals = normals,
                RigidBoneIndex = targetBoneIndex
            };
        }

        return slice with
        {
            Parts = parts,
            Skeleton = slice.Skeleton is null
                ? null
                : TransformSkeletonWorld(slice.Skeleton, transform)
        };
    }

    private static GrannySkeleton TransformSkeletonWorld(GrannySkeleton skeleton, Matrix4x4 transform)
    {
        var bones = new GrannyBone[skeleton.Bones.Length];
        for (var index = 0; index < bones.Length; index++)
            bones[index] = skeleton.Bones[index] with
            {
                RestWorld = skeleton.Bones[index].RestWorld * transform
            };

        return CreateSkeleton(bones, skeleton.BoneTieBones);
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
        var formMeshData = ReadFormMeshData(data, descriptors);
        var texturePolygonBlocks = ReadTexturePolygonBlocks(
            data,
            listOffset,
            descriptorScanLength,
            descriptors,
            formMeshData.SourceMeshMap);
        var texturePolygons = texturePolygonBlocks.SelectMany(static block => block.Polygons).ToArray();
        var parts = new List<ParsedMeshPart>();
        var seenMeshes = new HashSet<MeshKey>();
        var sourceMeshDescriptors = EnumerateSourceMeshDescriptors(descriptors);
        for (var sourceMeshIndex = 0; sourceMeshIndex < sourceMeshDescriptors.Count; sourceMeshIndex++)
        {
            // Empty Granny mesh entries still occupy an index used by the form table.
            var descriptor = descriptors[sourceMeshDescriptors[sourceMeshIndex]];
            var boneTieBones = formMeshData.BoneTieBonesBySourceMesh.TryGetValue(
                sourceMeshIndex,
                out var mappedBoneTieBones)
                ? mappedBoneTieBones
                : [];

            if (!TryReadMeshPart(
                    data,
                    descriptor.DescriptorOffset,
                    listOffset,
                    descriptorScanLength,
                    sourceMeshIndex,
                    boneTieBones,
                    out var part))
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
                ReadSkeleton(data, descriptorScanLength, descriptors));
    }

    private static List<int> EnumerateSourceMeshDescriptors(IReadOnlyList<ItemDescriptor> descriptors)
    {
        var meshDescriptors = new List<int>();
        var meshListIndex = FindImmediateChild(descriptors, -1, MeshListChunk);
        if (meshListIndex >= 0)
        {
            foreach (var descriptorIndex in EnumerateImmediateChildren(descriptors, meshListIndex))
            {
                if (descriptors[descriptorIndex].Chunk == MeshChunk)
                    meshDescriptors.Add(descriptorIndex);
            }

            if (meshDescriptors.Count > 0)
                return meshDescriptors;
        }

        for (var descriptorIndex = 0; descriptorIndex < descriptors.Count; descriptorIndex++)
        {
            if (descriptors[descriptorIndex].Chunk == MeshChunk)
                meshDescriptors.Add(descriptorIndex);
        }

        return meshDescriptors;
    }

    private static FormMeshData ReadFormMeshData(
        ReadOnlySpan<byte> data,
        IReadOnlyList<ItemDescriptor> descriptors)
    {
        var formSectionIndex = FindImmediateChild(descriptors, -1, FormSectionChunk);
        if (formSectionIndex < 0)
            return new FormMeshData([], new Dictionary<int, uint[]>());

        var meshIndexes = new List<int>();
        var boneTieBonesBySourceMesh = new Dictionary<int, uint[]>();
        var end = DescendantEndExclusive(descriptors, formSectionIndex);
        for (var descriptorIndex = formSectionIndex + 1; descriptorIndex < end; descriptorIndex++)
        {
            var descriptor = descriptors[descriptorIndex];
            if (descriptor.Chunk != FormMeshChunk)
                continue;

            // Form-mesh records store a one-based source mesh index and own that mesh's bone bindings.
            var sourceMeshIndex = descriptor.DataOffset >= 0 &&
                                  descriptor.DataOffset + 4 <= data.Length
                ? (long)ReadUInt32(data, descriptor.DataOffset) - 1
                : -1;
            var validSourceMeshIndex = sourceMeshIndex is >= 0 and <= int.MaxValue
                ? (int)sourceMeshIndex
                : -1;
            meshIndexes.Add(validSourceMeshIndex);

            if (validSourceMeshIndex < 0)
                continue;

            var bones = new List<uint>();
            var formMeshEnd = DescendantEndExclusive(descriptors, descriptorIndex);
            for (var childIndex = descriptorIndex + 1; childIndex < formMeshEnd; childIndex++)
            {
                var child = descriptors[childIndex];
                if (child.Chunk == BoneTieChunk &&
                    child.DataOffset >= 0 &&
                    child.DataOffset + 4 <= data.Length)
                    bones.Add(ReadUInt32(data, child.DataOffset));
            }

            if (bones.Count > 0)
                boneTieBonesBySourceMesh.TryAdd(validSourceMeshIndex, bones.ToArray());
        }

        return new FormMeshData(meshIndexes.ToArray(), boneTieBonesBySourceMesh);
    }

    private static bool TryReadMeshPart(
        ReadOnlySpan<byte> data,
        int meshDescriptorOffset,
        int listBase,
        int descriptorScanLength,
        int sourceMeshIndex,
        uint[] boneTieBones,
        out ParsedMeshPart part)
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
        var normalCount = (textureOffset - normalOffset) / 12;
        var textureCount = (weightOffset - textureOffset - 4) / TextureCoordinateStride;
        var polygonCount = (meshIdOffset - polygonOffset) / 24;
        if (pointCount <= 0 || normalCount < 0 || textureCount < 0 || polygonCount <= 0)
            return false;

        var positions = new Vector3[pointCount];
        for (var i = 0; i < positions.Length; i++)
        {
            var offset = pointOffset + i * 12;
            positions[i] = new Vector3(ReadSingle(data, offset), ReadSingle(data, offset + 4), ReadSingle(data, offset + 8));
            if (!IsFinite(positions[i]))
                return false;
        }

        var normals = new Vector3[normalCount];
        for (var i = 0; i < normals.Length; i++)
        {
            var offset = normalOffset + i * 12;
            normals[i] = new Vector3(ReadSingle(data, offset), ReadSingle(data, offset + 4), ReadSingle(data, offset + 8));
            if (!IsFinite(normals[i]))
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
                ReadUInt32(data, offset + 8),
                ReadUInt32(data, offset + 12),
                ReadUInt32(data, offset + 16),
                ReadUInt32(data, offset + 20));
        }

        part = new ParsedMeshPart(
            sourceMeshIndex,
            pointOffset,
            polygonOffset,
            positions,
            normals,
            texCoords,
            polygons,
            ReadVertexWeights(data, weightOffset, polygonOffset, pointCount),
            boneTieBones,
            [],
            -1);
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
        var translations = new Vector3[boneDescriptors.Count];
        var rotations = new Quaternion[boneDescriptors.Count];
        var scaleShears = new Matrix4x4[boneDescriptors.Count];
        for (var boneIndex = 0; boneIndex < boneDescriptors.Count; boneIndex++)
        {
            var dataOffset = boneDescriptors[boneIndex].DataOffset;
            if (dataOffset < 0 || dataOffset + 68 > data.Length)
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

            var scaleShear = ReadScaleShear(data, dataOffset + 32);
            if (!IsFinite(scaleShear))
                return null;

            translations[boneIndex] = translation;
            rotations[boneIndex] = rotation.LengthSquared() > 0.000001f
                ? Quaternion.Normalize(rotation)
                : Quaternion.Identity;
            scaleShears[boneIndex] = scaleShear;
            localTransforms[boneIndex] = CreateBoneTransform(rotation, translation, scaleShear);
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
        for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            bones[boneIndex] = new GrannyBone(
                names[boneIndex],
                parents[boneIndex],
                translations[boneIndex],
                rotations[boneIndex],
                scaleShears[boneIndex],
                localTransforms[boneIndex],
                worldTransforms[boneIndex]);
        }

        return CreateSkeleton(bones, boneTieBones);
    }

    private static GrannySkeleton CreateSkeleton(GrannyBone[] bones, uint[] boneTieBones)
    {
        var bonesByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            if (!string.IsNullOrWhiteSpace(bones[boneIndex].Name))
                bonesByName.TryAdd(bones[boneIndex].Name, boneIndex);
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
        var listIndex = FindImmediateChild(descriptors, -1, FormSectionChunk);
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
        var listIndex = FindImmediateChild(descriptors, -1, FormSectionChunk);
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

    private static Matrix4x4 CreateBoneTransform(Quaternion rotation, Vector3 translation, Matrix4x4 scaleShear)
    {
        var normalizedRotation = rotation.LengthSquared() > 0.000001f
            ? Quaternion.Normalize(rotation)
            : Quaternion.Identity;
        return scaleShear *
               Matrix4x4.CreateFromQuaternion(normalizedRotation) *
               Matrix4x4.CreateTranslation(translation);
    }

    private static Matrix4x4 ReadScaleShear(ReadOnlySpan<byte> data, int offset)
    {
        var m00 = ReadSingle(data, offset + 0);
        var m01 = ReadSingle(data, offset + 4);
        var m02 = ReadSingle(data, offset + 8);
        var m10 = ReadSingle(data, offset + 12);
        var m11 = ReadSingle(data, offset + 16);
        var m12 = ReadSingle(data, offset + 20);
        var m20 = ReadSingle(data, offset + 24);
        var m21 = ReadSingle(data, offset + 28);
        var m22 = ReadSingle(data, offset + 32);

        // Granny stores the 3x3 scale/shear block in the column-vector transform.
        // Matrix4x4/Vector3.Transform use row-vector order, so store the transpose.
        return new Matrix4x4(
            m00, m10, m20, 0.0f,
            m01, m11, m21, 0.0f,
            m02, m12, m22, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);
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

        var bounds = CalculateBounds(slice.Parts);
        var axis = VerticalAxis(bounds.Max - bounds.Min);
        var sideAxis = DetermineSideAxis(sourceSkeleton, axis);
        var center = Axis((bounds.Min + bounds.Max) * 0.5f, sideAxis);
        var partTieTransforms = new Matrix4x4?[slice.Parts.Length][];
        var partTargetBoneIndices = new int[slice.Parts.Length][];
        var mappedTieCount = 0;
        for (var partIndex = 0; partIndex < slice.Parts.Length; partIndex++)
        {
            var part = slice.Parts[partIndex];
            IReadOnlyList<uint> boneTieBones = part.BoneTieBones.Length > 0
                ? part.BoneTieBones
                : sourceSkeleton.BoneTieBones;
            var sideRemap = DetermineSingleSidedMirrorRemap(
                part,
                sourceSkeleton,
                boneTieBones,
                sideAxis,
                center);
            var tieTransforms = new Matrix4x4?[boneTieBones.Count];
            var targetBoneIndices = new int[boneTieBones.Count];
            Array.Fill(targetBoneIndices, -1);
            partTieTransforms[partIndex] = tieTransforms;
            partTargetBoneIndices[partIndex] = targetBoneIndices;

            for (var tieIndex = 0; tieIndex < boneTieBones.Count; tieIndex++)
            {
                var sourceBoneIndex = boneTieBones[tieIndex];
                if (sourceBoneIndex >= sourceSkeleton.Bones.Length)
                    continue;

                var sourceBone = sourceSkeleton.Bones[sourceBoneIndex];
                var targetBoneName = RemapSideBoneName(sourceBone.Name, sideRemap);
                var sourceRestBone = sourceBone;
                if (!string.Equals(targetBoneName, sourceBone.Name, StringComparison.Ordinal) &&
                    sourceSkeleton.BonesByName.TryGetValue(targetBoneName, out var remappedSourceBoneIndex))
                    sourceRestBone = sourceSkeleton.Bones[remappedSourceBoneIndex];

                if (string.IsNullOrWhiteSpace(targetBoneName) ||
                    !targetSkeleton.BonesByName.TryGetValue(targetBoneName, out var targetBoneIndex) ||
                    !Matrix4x4.Invert(sourceRestBone.RestWorld, out var inverseSourceRest))
                    continue;

                // System.Numerics transforms row vectors, so the column-vector Granny skinning order is reversed.
                tieTransforms[tieIndex] = inverseSourceRest * targetSkeleton.Bones[targetBoneIndex].RestWorld;
                targetBoneIndices[tieIndex] = targetBoneIndex;
                mappedTieCount++;
            }
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
                    if (weight.BoneTieIndex >= partTieTransforms[partIndex].Length ||
                        partTieTransforms[partIndex][weight.BoneTieIndex] is not { } transform ||
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

            var normalSourceVertices = new int[part.Normals.Length];
            Array.Fill(normalSourceVertices, -1);
            foreach (var polygon in part.Polygons)
            {
                AssociateNormalWithVertex(normalSourceVertices, polygon.NormalA, polygon.A);
                AssociateNormalWithVertex(normalSourceVertices, polygon.NormalB, polygon.B);
                AssociateNormalWithVertex(normalSourceVertices, polygon.NormalC, polygon.C);
            }

            var normals = new Vector3[part.Normals.Length];
            for (var normalIndex = 0; normalIndex < normals.Length; normalIndex++)
            {
                var sourceNormal = part.Normals[normalIndex];
                var sourceVertexIndex = normalSourceVertices[normalIndex];
                if ((uint)sourceVertexIndex >= (uint)part.Weights.Length)
                {
                    normals[normalIndex] = sourceNormal;
                    continue;
                }

                var transformedNormal = Vector3.Zero;
                var totalWeight = 0.0f;
                foreach (var weight in part.Weights[sourceVertexIndex])
                {
                    if (weight.BoneTieIndex >= partTieTransforms[partIndex].Length ||
                        partTieTransforms[partIndex][weight.BoneTieIndex] is not { } transform ||
                        !float.IsFinite(weight.Weight) || weight.Weight <= 0.0f)
                        continue;

                    transformedNormal += Vector3.TransformNormal(sourceNormal, transform) * weight.Weight;
                    totalWeight += weight.Weight;
                }

                normals[normalIndex] = totalWeight > 0.000001f
                    ? NormalizeOrZero(transformedNormal / totalWeight)
                    : sourceNormal;
            }

            parts[partIndex] = part with
            {
                Positions = positions,
                Normals = normals,
                TargetBoneIndices = partTargetBoneIndices[partIndex]
            };
        }

        return slice with
        {
            Parts = parts
        };
    }

    private static void AssociateNormalWithVertex(int[] mapping, uint normalIndex, uint vertexIndex)
    {
        if (normalIndex < mapping.Length && vertexIndex <= int.MaxValue && mapping[normalIndex] < 0)
            mapping[normalIndex] = checked((int)vertexIndex);
    }

    private static int DetermineSideAxis(GrannySkeleton skeleton, int verticalAxis)
    {
        var score = new float[3];
        for (var boneIndex = 0; boneIndex < skeleton.Bones.Length; boneIndex++)
        {
            var bone = skeleton.Bones[boneIndex];
            if (!IsLeftSideBoneName(bone.Name))
                continue;

            var rightName = bone.Name.Replace(" L ", " R ", StringComparison.Ordinal);
            if (!skeleton.BonesByName.TryGetValue(rightName, out var rightBoneIndex))
                continue;

            var leftPosition = new Vector3(bone.RestWorld.M41, bone.RestWorld.M42, bone.RestWorld.M43);
            var rightBone = skeleton.Bones[rightBoneIndex];
            var rightPosition = new Vector3(rightBone.RestWorld.M41, rightBone.RestWorld.M42, rightBone.RestWorld.M43);
            var delta = Vector3.Abs(leftPosition - rightPosition);
            score[0] += delta.X;
            score[1] += delta.Y;
            score[2] += delta.Z;
        }

        score[verticalAxis] = -1.0f;
        var bestAxis = (verticalAxis + 1) % 3;
        for (var axis = 0; axis < score.Length; axis++)
        {
            if (score[axis] > score[bestAxis])
                bestAxis = axis;
        }

        return bestAxis;
    }

    private static SideRemap DetermineSingleSidedMirrorRemap(
        ParsedMeshPart part,
        GrannySkeleton sourceSkeleton,
        IReadOnlyList<uint> boneTieBones,
        int horizontalAxis,
        float center)
    {
        var usesLeft = false;
        var usesRight = false;
        var totalWeight = 0.0f;
        var sideWeight = 0.0f;
        var weightedBoneCenter = 0.0f;
        foreach (var vertexWeights in part.Weights)
        {
            foreach (var weight in vertexWeights)
            {
                if (weight.BoneTieIndex >= boneTieBones.Count)
                    continue;

                var boneIndex = boneTieBones[(int)weight.BoneTieIndex];
                if (boneIndex >= sourceSkeleton.Bones.Length)
                    continue;

                var bone = sourceSkeleton.Bones[boneIndex];
                var influence = float.IsFinite(weight.Weight) && weight.Weight > 0.0f
                    ? weight.Weight
                    : 1.0f;
                totalWeight += influence;

                var name = bone.Name;
                var left = IsLeftSideBoneName(name);
                var right = IsRightSideBoneName(name);
                usesLeft |= left;
                usesRight |= right;
                if (!left && !right)
                    continue;

                weightedBoneCenter += Axis(new Vector3(bone.RestWorld.M41, bone.RestWorld.M42, bone.RestWorld.M43), horizontalAxis) * influence;
                sideWeight += influence;
            }
        }

        if (usesLeft == usesRight)
            return SideRemap.None;
        if (sideWeight < totalWeight * 0.5f)
            return SideRemap.None;

        var partCenter = 0.0f;
        for (var i = 0; i < part.Positions.Length; i++)
            partCenter += Axis(part.Positions[i], horizontalAxis);
        partCenter /= Math.Max(1, part.Positions.Length);

        if (sideWeight <= 0.000001f)
            return SideRemap.None;

        weightedBoneCenter /= sideWeight;
        const float mirrorSideThreshold = 0.0001f;
        var partSide = partCenter - center;
        var boneSide = weightedBoneCenter - center;
        if (MathF.Abs(partSide) <= mirrorSideThreshold ||
            MathF.Abs(boneSide) <= mirrorSideThreshold ||
            partSide * boneSide >= 0.0f)
            return SideRemap.None;

        if (usesRight)
            return SideRemap.RightToLeft;
        if (usesLeft)
            return SideRemap.LeftToRight;

        return SideRemap.None;
    }

    private static string RemapSideBoneName(string name, SideRemap sideRemap) =>
        sideRemap switch
        {
            SideRemap.RightToLeft when IsRightSideBoneName(name) => name.Replace(" R ", " L ", StringComparison.Ordinal),
            SideRemap.LeftToRight when IsLeftSideBoneName(name) => name.Replace(" L ", " R ", StringComparison.Ordinal),
            _ => name
        };

    private static bool IsLeftSideBoneName(string name) =>
        name.Contains(" L ", StringComparison.Ordinal);

    private static bool IsRightSideBoneName(string name) =>
        name.Contains(" R ", StringComparison.Ordinal);

    private static BuiltMesh BuildMesh(
        IReadOnlyList<ParsedMeshSlice> slices,
        IReadOnlyList<ParsedMeshPart>? projectionParts = null,
        GrannySkeleton? skinSkeleton = null)
    {
        var allParts = slices.SelectMany(static slice => slice.Parts).ToArray();
        var bounds = CalculateBounds(projectionParts ?? allParts);
        var axis = VerticalAxis(bounds.Max - bounds.Min);
        var horizontal0 = (axis + 1) % 3;
        var horizontal1 = (axis + 2) % 3;
        var center = (bounds.Min + bounds.Max) * 0.5f;

        var vertices = new List<VertexPositionNormalTexture>();
        var indices = new List<ushort>();
        var surfaces = new List<MeshSurface>();
        var skinVertices = skinSkeleton is null ? null : new List<GrnSkinVertex>();

        foreach (var slice in slices)
        {
            var sliceIndexStart = indices.Count;
            var sliceSurfaces = BuildTexturedMeshFromBlocks(
                slice.Parts,
                slice.TexturePolygonBlocks,
                slice.TextureNames,
                bounds.Min,
                center,
                axis,
                horizontal0,
                horizontal1,
                vertices,
                indices,
                skinVertices);

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
                    vertices,
                    indices,
                    skinVertices);

            surfaces.AddRange(sliceSurfaces);
        }

        var vertexArray = vertices.ToArray();
        var indexArray = indices.ToArray();
        FillMissingNormals(vertexArray, indexArray);
        var mesh = new Mesh(vertexArray, indexArray)
        {
            Surfaces = ClipSurfaceRanges(surfaces, indexArray.Length)
        };
        if (skinSkeleton is null || skinVertices is null || skinVertices.Count != vertexArray.Length)
            return new BuiltMesh(mesh, null);

        var publicBones = skinSkeleton.Bones.Select(static bone => new GrnBone(
            bone.Name,
            bone.ParentIndex,
            bone.RestTranslation,
            bone.RestRotation,
            bone.RestScaleShear,
            bone.RestLocal,
            bone.RestWorld)).ToArray();
        var projection = new GrnMeshProjection(bounds.Min, center, axis, horizontal0, horizontal1);
        var finalSkinVertices = skinVertices.ToArray();
        for (var vertexIndex = 0; vertexIndex < finalSkinVertices.Length; vertexIndex++)
        {
            if (finalSkinVertices[vertexIndex].BindNormal.LengthSquared() <= 0.000001f)
            {
                finalSkinVertices[vertexIndex] = finalSkinVertices[vertexIndex] with
                {
                    BindNormal = NormalizeOrZero(projection.UnprojectDirection(vertexArray[vertexIndex].Normal))
                };
            }
        }

        var skin = new GrnMeshSkin(
            new GrnSkeleton(publicBones),
            finalSkinVertices,
            projection);
        return new BuiltMesh(mesh, skin);
    }

    private static void FillMissingNormals(
        VertexPositionNormalTexture[] vertices,
        ushort[] indices)
    {
        if (vertices.All(static vertex => vertex.Normal.LengthSquared() > 0.000001f))
            return;

        var generated = (VertexPositionNormalTexture[])vertices.Clone();
        GrnStaticMeshExtractor.RecalculateNormals(generated, indices);
        for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
        {
            if (vertices[vertexIndex].Normal.LengthSquared() <= 0.000001f)
                vertices[vertexIndex] = vertices[vertexIndex] with { Normal = generated[vertexIndex].Normal };
        }
    }

    private static int CountPolygons(IEnumerable<ParsedMeshPart> parts) =>
        parts.Sum(static part => part.Polygons.Length);

    private static List<MeshSurface> BuildTexturedMeshFromBlocks(
        IReadOnlyList<ParsedMeshPart> parts,
        IReadOnlyList<TexturePolygonBlock> texturePolygonBlocks,
        IReadOnlyList<string> textureNames,
        Vector3 min,
        Vector3 center,
        int verticalAxis,
        int horizontalAxis0,
        int horizontalAxis1,
        List<VertexPositionNormalTexture> vertices,
        List<ushort> indices,
        List<GrnSkinVertex>? skinVertices)
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
                : BlockFitsPart(parts, block.FormSlot, block)
                    ? block.FormSlot
                    : SelectTextureBlockPart(parts, block, usedPolygons);
            if (partIndex < 0)
                continue;

            var part = parts[partIndex];
            var textureName = TextureNameForBlock(block, blockIndex, textureNames);
            var indexStart = indices.Count;
            foreach (var texturePolygon in block.Polygons)
            {
                if (vertices.Count + 3 > MaximumMeshVertices)
                    break;

                if (texturePolygon.A >= part.Polygons.Length ||
                    texturePolygon.B >= part.TextureCoordinates.Length ||
                    texturePolygon.C >= part.TextureCoordinates.Length ||
                    texturePolygon.D >= part.TextureCoordinates.Length)
                    continue;

                if (!usedPolygons[partIndex].Add(texturePolygon.A))
                    continue;

                var polygon = part.Polygons[checked((int)texturePolygon.A)];
                if (!IsValidPolygon(part, polygon))
                    continue;

                AddCorner(part, polygon.A, polygon.NormalA, texturePolygon.B, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
                AddCorner(part, polygon.B, polygon.NormalB, texturePolygon.C, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
                AddCorner(part, polygon.C, polygon.NormalC, texturePolygon.D, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
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

                AddCorner(part, polygon.A, polygon.NormalA, uint.MaxValue, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
                AddCorner(part, polygon.B, polygon.NormalB, uint.MaxValue, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
                AddCorner(part, polygon.C, polygon.NormalC, uint.MaxValue, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
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
        var partsBySourceMesh = new Dictionary<int, int>();
        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
            partsBySourceMesh.TryAdd(parts[partIndex].SourceMeshIndex, partIndex);

        for (var blockIndex = 0; blockIndex < texturePolygonBlocks.Count; blockIndex++)
        {
            var sourceMeshIndex = texturePolygonBlocks[blockIndex].SourceMeshIndex;
            if (sourceMeshIndex >= 0 &&
                partsBySourceMesh.TryGetValue(sourceMeshIndex, out var partIndex))
                assignments[blockIndex] = partIndex;
        }

        var usedParts = new bool[parts.Count];
        foreach (var partIndex in assignments)
        {
            if (partIndex >= 0)
                usedParts[partIndex] = true;
        }

        for (var groupStart = 0; groupStart < texturePolygonBlocks.Count;)
        {
            if (assignments[groupStart] >= 0)
            {
                groupStart++;
                continue;
            }

            var polygonCount = 0;
            var assignedPart = -1;
            var groupEnd = -1;
            for (var blockIndex = groupStart; blockIndex < texturePolygonBlocks.Count; blockIndex++)
            {
                if (assignments[blockIndex] >= 0)
                    break;

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
        List<VertexPositionNormalTexture> vertices,
        List<ushort> indices,
        List<GrnSkinVertex>? skinVertices)
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

                AddCorner(part, polygon.A, polygon.NormalA, texturePolygon.B, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
                AddCorner(part, polygon.B, polygon.NormalB, texturePolygon.C, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
                AddCorner(part, polygon.C, polygon.NormalC, texturePolygon.D, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
                globalPolygonIndex++;
            }
        }

        return surfaceRanges;
    }

    private static void AddCorner(
        ParsedMeshPart part,
        uint positionIndex,
        uint normalIndex,
        uint textureIndex,
        Vector3 min,
        Vector3 center,
        int verticalAxis,
        int horizontalAxis0,
        int horizontalAxis1,
        List<VertexPositionNormalTexture> vertices,
        List<ushort> indices,
        List<GrnSkinVertex>? skinVertices)
    {
        if (positionIndex >= part.Positions.Length)
            return;

        var source = part.Positions[positionIndex];
        var position = new Vector3(
            Axis(source, horizontalAxis0) - Axis(center, horizontalAxis0),
            Axis(source, horizontalAxis1) - Axis(center, horizontalAxis1),
            Axis(source, verticalAxis) - Axis(min, verticalAxis));

        var texCoord = textureIndex < part.TextureCoordinates.Length
            ? SanitizeTexCoord(part.TextureCoordinates[textureIndex])
            : Vector2.Zero;
        var sourceNormal = normalIndex < part.Normals.Length
            ? NormalizeOrZero(part.Normals[normalIndex])
            : Vector3.Zero;
        var normal = new Vector3(
            Axis(sourceNormal, horizontalAxis0),
            Axis(sourceNormal, horizontalAxis1),
            Axis(sourceNormal, verticalAxis));

        indices.Add(checked((ushort)vertices.Count));
        vertices.Add(new VertexPositionNormalTexture(position, NormalizeOrZero(normal), texCoord));
        skinVertices?.Add(CreateSkinVertex(part, checked((int)positionIndex), sourceNormal));
    }

    private static GrnSkinVertex CreateSkinVertex(
        ParsedMeshPart part,
        int positionIndex,
        Vector3 bindNormal)
    {
        if (part.RigidBoneIndex >= 0)
        {
            return new GrnSkinVertex(
                part.Positions[positionIndex],
                bindNormal,
                [new GrnBoneWeight(part.RigidBoneIndex, 1.0f)],
                UsesRigidBoneTransform: true);
        }

        if ((uint)positionIndex >= (uint)part.Weights.Length || part.TargetBoneIndices.Length == 0)
            return new GrnSkinVertex(part.Positions[positionIndex], bindNormal, []);

        var sourceWeights = part.Weights[positionIndex];
        if (sourceWeights.Length == 0)
            return new GrnSkinVertex(part.Positions[positionIndex], bindNormal, []);

        var combined = new Dictionary<int, float>();
        foreach (var sourceWeight in sourceWeights)
        {
            if (sourceWeight.BoneTieIndex >= part.TargetBoneIndices.Length ||
                !float.IsFinite(sourceWeight.Weight) || sourceWeight.Weight <= 0.0f)
                continue;

            var targetBoneIndex = part.TargetBoneIndices[sourceWeight.BoneTieIndex];
            if (targetBoneIndex < 0)
                continue;
            combined[targetBoneIndex] = combined.GetValueOrDefault(targetBoneIndex) + sourceWeight.Weight;
        }

        return new GrnSkinVertex(
            part.Positions[positionIndex],
            bindNormal,
            combined.Select(static pair => new GrnBoneWeight(pair.Key, pair.Value)).ToArray());
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
        IReadOnlyList<ItemDescriptor> descriptors,
        IReadOnlyList<int> formMeshMap)
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

                // Render passes address a form slot, not the mesh array directly.
                var formSlotValue = ReadUInt32(data, group.DataOffset);
                var textureIndexValue = ReadUInt32(data, group.DataOffset + 4);
                var formSlot = formSlotValue <= int.MaxValue ? (int)formSlotValue : -1;
                var sourceMeshIndex = (uint)formSlot < (uint)formMeshMap.Count
                    ? formMeshMap[formSlot]
                    : -1;
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
                        formSlot,
                        sourceMeshIndex,
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
        int formSlot,
        int sourceMeshIndex,
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

        return new TexturePolygonBlock(polygons, formSlot, sourceMeshIndex, textureIndex);
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

    private static Vector3 NormalizeOrZero(Vector3 value) =>
        IsFinite(value) && value.LengthSquared() > 0.000001f
            ? Vector3.Normalize(value)
            : Vector3.Zero;

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    private static bool IsFinite(Matrix4x4 value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
        float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
        float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
        float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));

    private static float ReadSingle(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.ToSingle(data.Slice(offset, 4));

    private sealed record ParsedMeshSlice(
        ParsedMeshPart[] Parts,
        TexturePolygon[] TexturePolygons,
        TexturePolygonBlock[] TexturePolygonBlocks,
        string[] TextureNames,
        GrannySkeleton? Skeleton);

    private readonly record struct ParsedMeshPart(
        int SourceMeshIndex,
        int PointOffset,
        int PolygonOffset,
        Vector3[] Positions,
        Vector3[] Normals,
        Vector2[] TextureCoordinates,
        GrannyPolygon[] Polygons,
        VertexWeight[][] Weights,
        uint[] BoneTieBones,
        int[] TargetBoneIndices,
        int RigidBoneIndex);

    private readonly record struct VertexWeight(uint BoneTieIndex, float Weight);

    private readonly record struct BuiltMesh(Mesh Mesh, GrnMeshSkin? Skin);

    private enum SideRemap
    {
        None,
        RightToLeft,
        LeftToRight
    }

    private sealed record GrannySkeleton(
        GrannyBone[] Bones,
        uint[] BoneTieBones,
        IReadOnlyDictionary<string, int> BonesByName);

    private readonly record struct GrannyBone(
        string Name,
        int ParentIndex,
        Vector3 RestTranslation,
        Quaternion RestRotation,
        Matrix4x4 RestScaleShear,
        Matrix4x4 RestLocal,
        Matrix4x4 RestWorld);

    private readonly record struct GrannyPolygon(
        uint A,
        uint B,
        uint C,
        uint NormalA,
        uint NormalB,
        uint NormalC);

    private readonly record struct TexturePolygon(uint A, uint B, uint C, uint D);

    private sealed record TexturePolygonBlock(
        TexturePolygon[] Polygons,
        int FormSlot,
        int SourceMeshIndex,
        int TextureIndex);

    private sealed record FormMeshData(
        int[] SourceMeshMap,
        IReadOnlyDictionary<int, uint[]> BoneTieBonesBySourceMesh);

    private readonly record struct ItemDescriptor(
        uint Chunk,
        uint RelativeOffset,
        int DataOffset,
        int DescendantCount,
        int DescriptorOffset);

    private readonly record struct MeshKey(int PointOffset, int PolygonOffset);

    private readonly record struct Bounds(Vector3 Min, Vector3 Max);
}
