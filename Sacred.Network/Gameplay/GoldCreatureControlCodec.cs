using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldCreatureControlCodec
{
    public static GoldGameEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var size = message.Opcode switch
        {
            GoldCreatureControlLayout.FightModeOpcode => GoldCreatureControlLayout.FightModeSize,
            GoldCreatureControlLayout.DialogOpcode => GoldCreatureControlLayout.DialogSize,
            _ => throw new ArgumentException("Expected a creature fight-mode or dialog update.", nameof(message))
        };
        if (message.Payload.Length != size) throw new InvalidDataException($"Gold creature control 0x{message.Opcode:X3} requires {size} bytes.");
        var owned = message with { Payload = message.Payload.ToArray() };
        var reference = BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(4));
        return message.Opcode == GoldCreatureControlLayout.FightModeOpcode
            ? new GoldCreatureFightModeEvent(owned, reference, value)
            : new GoldCreatureDialogEvent(owned, reference, value, BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(8)));
    }

    public static GoldMessage CreateFightMode(byte slot, uint reference, uint flags) => Create(slot, reference, flags, null);
    public static GoldMessage CreateDialog(byte slot, uint reference, uint dialogIndex, uint objectFlags) => Create(slot, reference, dialogIndex, objectFlags);

    private static GoldMessage Create(byte slot, uint reference, uint value, uint? flags)
    {
        if (slot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(slot));
        var payload = new byte[flags.HasValue ? GoldCreatureControlLayout.DialogSize : GoldCreatureControlLayout.FightModeSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, reference);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), value);
        if (flags is { } objectFlags) BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), objectFlags);
        return new(flags.HasValue ? GoldCreatureControlLayout.DialogOpcode : GoldCreatureControlLayout.FightModeOpcode, slot, slot, 0, payload);
    }
}
