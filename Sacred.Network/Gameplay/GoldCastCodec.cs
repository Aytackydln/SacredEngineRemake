using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldCastCodec
{
    public static GoldCreatureCastEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var bytes = message.Payload.AsSpan();
        if (message.Opcode != GoldCastLayout.Opcode || bytes.Length < GoldCastLayout.ActionOffset)
            throw new InvalidDataException("Truncated Gold spell action.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[GoldCastLayout.ActionBytesOffset..]);
        if (count > bytes.Length - GoldCastLayout.ActionOffset)
            throw new InvalidDataException("Gold spell action exceeds its packet.");
        return new(message with { Payload = bytes.ToArray() }, BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            bytes.Slice(GoldCastLayout.ActionOffset, (int)count).ToArray(),
            bytes[(GoldCastLayout.ActionOffset + (int)count)..].ToArray());
    }
}
