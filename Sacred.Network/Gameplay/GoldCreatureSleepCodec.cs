using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldCreatureSleepCodec
{
    public static GoldCreatureSleepEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != GoldCreatureSleepLayout.Opcode || message.Payload.Length != GoldCreatureSleepLayout.Size)
            throw new InvalidDataException("Gold creature sleep requires exactly eight payload bytes.");
        var owned = message with { Payload = message.Payload.ToArray() };
        return new(owned, BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload),
            BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(GoldCreatureSleepLayout.ClockOffsetOffset)));
    }
}
