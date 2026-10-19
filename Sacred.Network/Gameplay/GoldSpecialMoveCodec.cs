using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldSpecialMoveCodec
{
    public static GoldCreatureSpecialMoveEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var bytes = message.Payload.AsSpan();
        if (message.Opcode != GoldSpecialMoveLayout.Opcode || bytes.Length < GoldSpecialMoveLayout.ActionOffset)
            throw new InvalidDataException("Truncated Gold special move.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[GoldSpecialMoveLayout.ActionBytesOffset..]);
        if (count > bytes.Length - GoldSpecialMoveLayout.ActionOffset)
            throw new InvalidDataException("Gold special-move action exceeds its packet.");
        return new(message with { Payload = bytes.ToArray() }, BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            bytes.Slice(GoldSpecialMoveLayout.ActionOffset, (int)count).ToArray(),
            bytes[(GoldSpecialMoveLayout.ActionOffset + (int)count)..].ToArray());
    }
}
