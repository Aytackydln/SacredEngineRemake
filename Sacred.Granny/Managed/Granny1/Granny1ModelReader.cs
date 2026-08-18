using System.Numerics;

namespace Sacred.Granny.Managed.Granny1;

public static partial class Granny1MeshExtractor
{
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
}

