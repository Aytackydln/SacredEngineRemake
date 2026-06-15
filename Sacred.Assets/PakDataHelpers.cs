using System.Runtime.InteropServices;
using System.Text;

namespace Sacred.Assets;

internal static class PakDataHelpers
{
    internal const int EntryDescriptorSize = 0x0C;

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

    internal static ReadOnlySpan<PakEntryDescriptor> ReadEntryDescriptors(
        ReadOnlySpan<byte> data,
        int headerSize,
        int count,
        string archiveName)
    {
        var byteLength = checked(count * EntryDescriptorSize);
        if (headerSize + byteLength > data.Length)
            throw new InvalidDataException($"{archiveName} is too small to contain {count} entry descriptors.");

        return MemoryMarshal.Cast<byte, PakEntryDescriptor>(data.Slice(headerSize, byteLength));
    }

    internal static PakEntryDescriptor[] ReadEntryDescriptors(Stream stream, int count, string archiveName)
    {
        var descriptors = new PakEntryDescriptor[count];
        var bytes = MemoryMarshal.AsBytes(descriptors.AsSpan());
        var expectedLength = count * EntryDescriptorSize;
        if (bytes.Length != expectedLength)
            throw new InvalidDataException($"{archiveName} descriptor layout is {bytes.Length / Math.Max(1, count)} bytes, expected {EntryDescriptorSize}.");

        stream.ReadExactly(bytes);
        return descriptors;
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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly record struct PakEntryDescriptor(uint Type, uint Offset, uint Size);
