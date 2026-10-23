using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldObjectQuestCodec
{
    public static GoldObjectQuestEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode is not (GoldObjectQuestLayout.NumberOpcode or GoldObjectQuestLayout.EffectOpcode))
            throw new ArgumentException("Expected an object quest tag update.", nameof(message));
        if (message.Payload.Length != GoldObjectQuestLayout.Size)
            throw new InvalidDataException("Gold object quest tag requires exactly eight bytes.");
        var owned = message with { Payload = message.Payload.ToArray() };
        return new(owned, BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload),
            BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(GoldObjectQuestLayout.ValueOffset)));
    }

    public static GoldMessage CreateNumber(byte slot, uint reference, short value) => Create(slot, reference, value, GoldObjectQuestLayout.NumberOpcode);
    public static GoldMessage CreateEffect(byte slot, uint reference, short value) => Create(slot, reference, value, GoldObjectQuestLayout.EffectOpcode);

    private static GoldMessage Create(byte slot, uint reference, short value, ushort opcode)
    {
        if (slot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(slot));
        var payload = new byte[GoldObjectQuestLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(GoldObjectQuestLayout.ValueOffset), value);
        return new(opcode, slot, slot, 0, payload);
    }
}
