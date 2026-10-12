using System.Buffers.Binary;

namespace Sacred.Network.Protocol;

/// <summary>The 16-byte Gold application header; all multibyte fields are little-endian.</summary>
public static class GoldMessageCodec
{
    public const int HeaderBytes = 16;
    public const int MaximumMessageBytes = 0x3FFFFE;
    private const uint LengthMask = 0x003FFFFF;

    public static byte[] Encode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(message.Payload);
        if (message.Sender > 31 || message.Origin > 31)
            throw new ArgumentOutOfRangeException(nameof(message), "Header identities occupy five bits each.");
        var size = checked(HeaderBytes + message.Payload.Length);
        if (size > MaximumMessageBytes) throw new ArgumentOutOfRangeException(nameof(message));
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes,
            (uint)size | ((uint)message.Sender << 22) | ((uint)message.Origin << 27));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), message.Sequence);
        "LH20"u8.CopyTo(bytes.AsSpan(10));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), message.Opcode);
        message.Payload.CopyTo(bytes, HeaderBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), SacredChecksum.ComputeMessage(bytes));
        return bytes;
    }

    public static IReadOnlyList<GoldMessage> DecodeBatch(ReadOnlySpan<byte> batch)
    {
        var result = new List<GoldMessage>();
        while (!batch.IsEmpty)
        {
            if (batch.Length < HeaderBytes) throw new InvalidDataException("Truncated Gold header.");
            var word = BinaryPrimitives.ReadUInt32LittleEndian(batch);
            var size = (int)(word & LengthMask);
            if (size < HeaderBytes || size > MaximumMessageBytes || size > batch.Length)
                throw new InvalidDataException("Invalid Gold message length.");
            var message = batch[..size];
            if (!message.Slice(10, 4).SequenceEqual("LH20"u8))
                throw new InvalidDataException("Incorrect Gold message marker.");
            if (BinaryPrimitives.ReadUInt32LittleEndian(message[4..]) != SacredChecksum.ComputeMessage(message))
                throw new InvalidDataException("Gold message checksum mismatch.");
            result.Add(new GoldMessage(BinaryPrimitives.ReadUInt16LittleEndian(message[14..]),
                (byte)((word >> 22) & 31), (byte)(word >> 27),
                BinaryPrimitives.ReadUInt16LittleEndian(message[8..]), message[HeaderBytes..].ToArray()));
            batch = batch[size..];
        }
        return result;
    }
}
