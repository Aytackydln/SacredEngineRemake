using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldWorldRegionValueCodec
{
    public static GoldWorldRegionValueEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode is not (GoldWorldRegionValueLayout.SetOpcode or GoldWorldRegionValueLayout.AddOpcode))
            throw new ArgumentException("Expected a Gold region value update.", nameof(message));
        if (message.Payload.Length != GoldWorldRegionValueLayout.Size)
            throw new InvalidDataException("Gold region value update requires exactly twelve bytes.");
        var owned = message with { Payload = message.Payload.ToArray() };
        return new(owned, BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload),
            BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(GoldWorldRegionValueLayout.SelectorOffset)),
            BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(GoldWorldRegionValueLayout.ValueOffset)));
    }

    public static GoldMessage CreateSet(byte slot, uint region, uint selector, uint value) => Create(slot, region, selector, value, false);
    public static GoldMessage CreateAdd(byte slot, uint region, uint selector, uint value) => Create(slot, region, selector, value, true);

    private static GoldMessage Create(byte slot, uint region, uint selector, uint value, bool add)
    {
        if (slot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(slot));
        var payload = new byte[GoldWorldRegionValueLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, unchecked((byte)region));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(GoldWorldRegionValueLayout.SelectorOffset), unchecked((ushort)selector));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(GoldWorldRegionValueLayout.ValueOffset), value);
        return new(add ? GoldWorldRegionValueLayout.AddOpcode : GoldWorldRegionValueLayout.SetOpcode, slot, slot, 0, payload);
    }
}
