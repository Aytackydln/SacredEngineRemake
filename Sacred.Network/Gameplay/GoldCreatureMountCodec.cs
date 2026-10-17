using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldCreatureMountCodec
{
    public static GoldCreatureMountEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != GoldCreatureMountLayout.Opcode) throw new ArgumentOutOfRangeException(nameof(message));
        if (message.Payload.Length != GoldCreatureMountLayout.Size)
            throw new InvalidDataException("Gold creature mount link requires exactly eight payload bytes.");
        return new(message, BinaryPrimitives.ReadUInt32LittleEndian(message.Payload),
            BinaryPrimitives.ReadUInt32LittleEndian(message.Payload.AsSpan(GoldCreatureMountLayout.SecondReferenceOffset)));
    }

    public static GoldMessage Create(byte playerSlot, uint firstReference, uint secondReference)
    {
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        var payload = new byte[GoldCreatureMountLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, firstReference);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(GoldCreatureMountLayout.SecondReferenceOffset), secondReference);
        return new(GoldCreatureMountLayout.Opcode, playerSlot, playerSlot, 0, payload);
    }
}
