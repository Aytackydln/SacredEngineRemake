using System.Buffers.Binary;
using System.IO.Compression;

namespace Sacred.Network.Gameplay;

/// <summary>801580's four-byte inner header: bit 27 selects raw rolling-XOR data; other bits are output size.</summary>
public static class GoldPackedProfileBlock
{
    public const int MaximumBytes = 0x3FFFFF;
    public static byte[] Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4) throw new InvalidDataException("Truncated Gold packed profile header.");
        var header = BinaryPrimitives.ReadUInt32LittleEndian(bytes); var size = header & ~0x08000000u;
        if (size > MaximumBytes) throw new InvalidDataException("Gold profile exceeds native output limit.");
        var result = new byte[(int)size];
        if ((header & 0x08000000) != 0)
        {
            if (bytes.Length - 4 < result.Length) throw new InvalidDataException("Truncated Gold raw profile.");
            bytes.Slice(4, result.Length).CopyTo(result); var previous = size;
            for (var offset = 0; offset + 4 <= result.Length; offset += 4)
            {
                var word = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(offset));
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), unchecked(previous + 0xB5D6C7A3) ^ word); previous = word;
            }
            return result;
        }
        using var input = new GoldProfileZlibInput(bytes[4..].ToArray());
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        try { zlib.ReadExactly(result); }
        catch (EndOfStreamException e) { throw new InvalidDataException("Truncated decompressed Gold profile.", e); }
        if (zlib.ReadByte() != -1) throw new InvalidDataException("Gold profile exceeds declared output size.");
        // ZLibStream permits EOF after output even when the checksum trailer is incomplete.
        if (input.Position < 6 || BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice((int)input.Position, 4)) != Adler32(result))
            throw new InvalidDataException("Incomplete Gold profile zlib trailer.");
        return result;
    }
    private static uint Adler32(ReadOnlySpan<byte> bytes)
    {
        uint a = 1, b = 0;
        foreach (var value in bytes) { a = (a + value) % 65521; b = (b + a) % 65521; }
        return (b << 16) | a;
    }
}
