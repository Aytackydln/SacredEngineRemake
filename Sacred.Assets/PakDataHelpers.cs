using System.Text;

namespace Sacred.Assets;

internal static class PakDataHelpers
{
    internal static int ReadEntryCount(ReadOnlySpan<byte> data, int headerSize, int descriptorSize, string archiveName)
    {
        var count32 = BitConverter.ToUInt32(data.Slice(4, 4));
        var count16 = BitConverter.ToUInt16(data.Slice(4, 2));
        var maxDescriptorCount = Math.Max(0, (data.Length - headerSize) / descriptorSize);

        if (count32 <= maxDescriptorCount)
            return (int)count32;
        if (count16 <= maxDescriptorCount)
            return count16;

        throw new InvalidDataException($"Cannot determine {archiveName} entry count. count16={count16}, count32={count32}, max={maxDescriptorCount}");
    }

    internal static string ReadCString(ReadOnlySpan<byte> data, int offset, int maxLength, Encoding encoding)
    {
        var end = offset;
        var maxEnd = Math.Min(data.Length, offset + maxLength);
        while (end < maxEnd && data[end] != 0)
            end++;

        return encoding.GetString(data.Slice(offset, end - offset));
    }
}