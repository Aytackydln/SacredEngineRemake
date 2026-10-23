using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldCreatureTextCodec
{
    public static GoldCreatureTextEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != GoldCreatureTextLayout.Opcode)
            throw new ArgumentException("Expected a creature text notification.", nameof(message));
        if (message.Payload.Length != GoldCreatureTextLayout.Size)
            throw new InvalidDataException("Gold creature text requires exactly eight bytes.");
        var owned = message with { Payload = message.Payload.ToArray() };
        return new(owned, BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload),
            BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(GoldCreatureTextLayout.TextIdOffset)));
    }

    public static GoldMessage Create(byte slot, uint reference, uint textId)
    {
        if (slot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(slot));
        var payload = new byte[GoldCreatureTextLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(GoldCreatureTextLayout.TextIdOffset), textId);
        return new(GoldCreatureTextLayout.Opcode, slot, slot, 0, payload);
    }
}
