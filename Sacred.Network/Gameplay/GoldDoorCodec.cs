using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>The word is a runtime object reference, independent of static and trigger identities.</summary>
public static class GoldDoorCodec
{
    public static GoldDoorEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode is not (GoldDoorLayout.OpenOpcode or GoldDoorLayout.CloseOpcode))
            throw new ArgumentException("Expected a Gold door message.", nameof(message));
        if (message.Payload.Length < GoldDoorLayout.PrefixSize)
            throw new InvalidDataException("Truncated Gold door reference.");
        var owned = message with { Payload = message.Payload.ToArray() };
        return new(owned, BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload),
            owned.Opcode == GoldDoorLayout.OpenOpcode, owned.Payload[GoldDoorLayout.PrefixSize..]);
    }

    public static GoldMessage Create(uint objectReference, bool open, byte sender, byte origin, ushort sequence = 0)
    {
        var payload = new byte[GoldDoorLayout.PrefixSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, objectReference);
        return new(open ? GoldDoorLayout.OpenOpcode : GoldDoorLayout.CloseOpcode, sender, origin, sequence, payload);
    }
}
