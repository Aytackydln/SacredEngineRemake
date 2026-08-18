namespace Sacred.Granny.Managed.Granny1;

public static partial class Granny1MeshExtractor
{
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
}

