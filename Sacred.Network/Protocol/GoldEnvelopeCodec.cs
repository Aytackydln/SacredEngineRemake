using System.Buffers.Binary;
using System.IO.Compression;

namespace Sacred.Network.Protocol;

/// <summary>The ten-byte game-batch envelope; discovery/lobby prefixes use another codec.</summary>
public static class GoldEnvelopeCodec
{
    public const int HeaderBytes = 10;
    public const int MaximumBodyBytes = 0x3FFFFE;
    public const int MaximumCompressedBytes = 0x3FFFF;

    public static byte[] Encode(ReadOnlySpan<byte> batch)
    {
        if (batch.Length is <= 0 or > MaximumBodyBytes) throw new ArgumentOutOfRangeException(nameof(batch));
        byte[] body;
        var compressed = 0;
        if (batch.Length < 80)
        {
            body = batch.ToArray();
            TransformRaw(body, true);
        }
        else
        {
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(batch);
            body = output.ToArray();
            compressed = body.Length;
            if (compressed > MaximumCompressedBytes) throw new InvalidDataException("Gold compressed batch exceeds its limit.");
        }
        var bytes = new byte[HeaderBytes + body.Length];
        WriteHeader(bytes, compressed, batch.Length, SacredChecksum.Compute(body));
        body.CopyTo(bytes, HeaderBytes);
        return bytes;
    }

    public static byte[] Decode(ReadOnlySpan<byte> envelope)
    {
        if (envelope.Length < HeaderBytes) throw new InvalidDataException("Truncated Gold envelope.");
        var (compressed, unpacked) = ReadLengths(envelope);
        if (compressed > MaximumCompressedBytes || unpacked is <= 0 or > MaximumBodyBytes)
            throw new InvalidDataException("Invalid Gold envelope lengths.");
        var body = envelope[HeaderBytes..];
        if (body.Length != (compressed == 0 ? unpacked : compressed))
            throw new InvalidDataException("Gold envelope body length mismatch.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(envelope[6..]) != SacredChecksum.Compute(body))
            throw new InvalidDataException("Gold envelope checksum mismatch.");
        if (compressed == 0)
        {
            var raw = body.ToArray();
            TransformRaw(raw, false);
            return raw;
        }
        using var input = new MemoryStream(body.ToArray(), writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        var result = new byte[unpacked];
        try { zlib.ReadExactly(result); }
        catch (EndOfStreamException e) { throw new InvalidDataException("Truncated Gold decompressed batch.", e); }
        if (zlib.ReadByte() != -1) throw new InvalidDataException("Gold batch exceeds declared decompressed length.");
        return result;
    }

    public static void WriteHeader(Span<byte> destination, int compressed, int unpacked, uint checksum)
    {
        if (destination.Length < HeaderBytes) throw new ArgumentException("Ten header bytes required.", nameof(destination));
        if (compressed is < 0 or > MaximumCompressedBytes || unpacked is <= 0 or > MaximumBodyBytes)
            throw new ArgumentOutOfRangeException(nameof(compressed));
        for (var i = 0; i < 5; i++)
            destination[i] = (byte)(((compressed >> (4 * i)) & 15) | (((unpacked >> (4 * (i + 1))) & 15) << 4));
        destination[5] = (byte)((unpacked & 15) | (((unpacked >> 24) & 15) << 4));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[6..], checksum);
    }

    private static (int Compressed, int Unpacked) ReadLengths(ReadOnlySpan<byte> bytes)
    {
        var compressed = 0;
        var unpacked = (bytes[5] & 15) | ((bytes[5] >> 4) << 24);
        for (var i = 0; i < 5; i++)
        {
            compressed |= (bytes[i] & 15) << (4 * i);
            unpacked |= (bytes[i] >> 4) << (4 * (i + 1));
        }
        return (compressed, unpacked);
    }

    private static void TransformRaw(Span<byte> bytes, bool encode)
    {
        var previous = (uint)bytes.Length;
        for (var offset = 0; offset + 4 <= bytes.Length; offset += 4)
        {
            var word = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
            var transformed = unchecked(previous + 0xB5D6C7A3) ^ word;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], transformed);
            previous = encode ? transformed : word;
        }
    }
}
