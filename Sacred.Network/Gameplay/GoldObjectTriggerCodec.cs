using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldObjectTriggerCodec
{
    public static GoldObjectTriggerEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != GoldObjectTriggerLayout.Opcode) throw new ArgumentOutOfRangeException(nameof(message));
        var payload = message.Payload.AsSpan();
        if (payload.Length != GoldObjectTriggerLayout.Size)
            throw new InvalidDataException("Gold object-trigger assignment requires exactly eight payload bytes.");
        return new(message, BinaryPrimitives.ReadUInt32LittleEndian(payload),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[GoldObjectTriggerLayout.TriggerReferenceOffset..]));
    }

    public static GoldMessage Create(byte playerSlot, uint objectReference, uint triggerReference)
    {
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        var payload = new byte[GoldObjectTriggerLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, objectReference);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(GoldObjectTriggerLayout.TriggerReferenceOffset), triggerReference);
        return new(GoldObjectTriggerLayout.Opcode, playerSlot, playerSlot, 0, payload);
    }
}
