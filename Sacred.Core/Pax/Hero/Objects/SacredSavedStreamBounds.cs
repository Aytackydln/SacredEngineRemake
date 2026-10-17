using System.Buffers.Binary;

namespace Sacred.Core.Pax.Hero.Objects;

internal static class SacredSavedStreamBounds
{
    public const byte SupportedVersion = 27;
    public static void RequireVersion(byte version)
    {
        if (version != SupportedVersion) throw new NotSupportedException($"Saved object format {version} has no verified reader; format 27 is supported.");
    }
    public static void Require(ReadOnlySpan<byte> payload, int size)
    {
        if (payload.Length < size) throw new InvalidDataException("Truncated saved object stream.");
    }
    public static int Advance(ReadOnlySpan<byte> payload, int position, uint count, int stride)
    {
        var end = (ulong)position + (ulong)count * (uint)stride;
        if (end > (ulong)payload.Length) throw new InvalidDataException("Truncated saved object records.");
        return (int)end;
    }
    public static int List(ReadOnlySpan<byte> payload, ref int position, int countBytes, int stride)
    {
        var data = Advance(payload, position, 1, countBytes);
        uint count = countBytes == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(payload[position..]) : BinaryPrimitives.ReadUInt32LittleEndian(payload[position..]);
        position = Advance(payload, data, count, stride);
        return (int)count;
    }
    public static ReadOnlySpan<byte> Record(byte[] payload, int index, int count, int offset, int stride)
    {
        if ((uint)index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        return payload.AsSpan(offset + index * stride, stride);
    }
}
