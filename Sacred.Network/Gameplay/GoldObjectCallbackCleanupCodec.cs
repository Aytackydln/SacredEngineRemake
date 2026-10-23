using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldObjectCallbackCleanupEvent(GoldMessage Message, uint ObjectReference) : GoldGameEvent(Message);

/// <summary>Gold 0x1C1 performs the native death-mode cleanup of an object's callback registrations.</summary>
public static class GoldObjectCallbackCleanupCodec
{
    public const ushort Opcode = 0x1C1;
    public static GoldObjectCallbackCleanupEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode || message.Payload.Length < 4)
            throw new InvalidDataException("Gold object callback cleanup requires a four-byte reference.");
        return new(message with { Payload = message.Payload.ToArray() }, BinaryPrimitives.ReadUInt32LittleEndian(message.Payload));
    }
}
