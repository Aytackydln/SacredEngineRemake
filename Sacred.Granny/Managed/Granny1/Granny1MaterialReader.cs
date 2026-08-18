using System.Text;

namespace Sacred.Granny.Managed.Granny1;

public static partial class Granny1MeshExtractor
{
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
}

