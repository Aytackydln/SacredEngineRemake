using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldCheatsEnabledEvent(GoldMessage Message, uint RawValue) : GoldGameEvent(Message)
{
    public bool Enabled => RawValue != 0;
}

/// <summary>Gold 1A8 observes the server's cheat-mode notification, independent of 01E game flags.</summary>
public static class GoldCheatsEnabledCodec
{
    public const ushort Opcode = 0x1A8;
    public static GoldCheatsEnabledEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode || message.Payload.Length < 4)
            throw new InvalidDataException("Gold cheat-mode notification requires an enabled word.");
        return new(message with { Payload = message.Payload.ToArray() }, BinaryPrimitives.ReadUInt32LittleEndian(message.Payload));
    }
}
