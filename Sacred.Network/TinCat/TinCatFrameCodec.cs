using System.Buffers.Binary;

namespace Sacred.Network.TinCat;

/// <summary>TCP framing verified in TinCat 0x1000CBC0 / 0x1000CC50.</summary>
public static class TinCatFrameCodec
{
    public const int HeaderBytes = 28;
    public const int MaximumPayloadBytes = 4 * 1024 * 1024;
    public const uint Magic = 0xDABAFBEF;

    public static byte[] Encode(TinCatFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(frame.Payload);
        if (frame.Payload.Length is <= 0 or > MaximumPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(frame));
        var bytes = new byte[HeaderBytes + frame.Payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), frame.Sender);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), frame.Recipient);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), frame.Type);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), frame.Reserved);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), frame.Payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), TinCatChecksum.Compute(frame.Payload));
        frame.Payload.CopyTo(bytes, HeaderBytes);
        return bytes;
    }

    public static int ReadPayloadLength(ReadOnlySpan<byte> header)
    {
        if (header.Length != HeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic)
            throw new InvalidDataException("Invalid TinCat frame header.");
        var size = BinaryPrimitives.ReadInt32LittleEndian(header[20..]);
        if (size is <= 0 or > MaximumPayloadBytes)
            throw new InvalidDataException("TinCat payload exceeds connection limits.");
        return size;
    }

    public static TinCatFrame Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes) throw new InvalidDataException("Truncated TinCat header.");
        var size = ReadPayloadLength(bytes[..HeaderBytes]);
        if (bytes.Length - HeaderBytes != size) throw new InvalidDataException("TinCat body length mismatch.");
        var payload = bytes[HeaderBytes..];
        if (TinCatChecksum.Compute(payload) != BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]))
            throw new InvalidDataException("TinCat body checksum mismatch.");
        return new TinCatFrame(BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]), payload.ToArray());
    }
}
