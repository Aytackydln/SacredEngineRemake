using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x7DD5D9 / 0x7DD613 forwards 2 or 8 to cObjectFX's particle notification.</summary>
public static class GoldObjectFxSignalCodec
{
    public const ushort RemoveOpcode = 0x182;
    public const ushort UsedOpcode = 0x184;
    public static GoldObjectFxSignalEvent Decode(GoldMessage message)
    {
        if (message.Payload.Length != 4 || message.Opcode is not (RemoveOpcode or UsedOpcode))
            throw new InvalidDataException("Invalid Gold object FX notification.");
        return new(message with { Payload = message.Payload.ToArray() },
            BinaryPrimitives.ReadUInt32LittleEndian(message.Payload) & 0x7FFFFFFF,
            message.Opcode == RemoveOpcode ? (ushort)2 : (ushort)8);
    }
}

public sealed record GoldObjectFxSignalEvent(GoldMessage Message, uint ObjectReference, ushort Notification) : GoldGameEvent(Message);
