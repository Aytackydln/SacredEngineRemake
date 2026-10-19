using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x18C container activation. The payload names a runtime instance, not an item resource.</summary>
public static class GoldContainerActivationCodec
{
    public const ushort Opcode = 0x18c;
    public const int Size = 4;
    /// <summary>cContainer +0x14 bit 14: opened/broken; suppresses repeated receive activation.</summary>
    public const uint ActivatedFlag = 0x4000;

    public static GoldContainerActivationEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode) throw new ArgumentException("Expected Gold container activation.", nameof(message));
        if (message.Payload.Length != Size) throw new InvalidDataException("Gold container activation needs exactly four bytes.");
        return new(message with { Payload = message.Payload.ToArray() }, BinaryPrimitives.ReadUInt32LittleEndian(message.Payload));
    }

    public static GoldMessage Create(uint objectReference, byte sender, byte origin, ushort sequence = 0)
    {
        var bytes = new byte[Size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, objectReference);
        return new(Opcode, sender, origin, sequence, bytes);
    }
}

public sealed record GoldContainerActivationEvent(GoldMessage Message, uint ObjectReference) : GoldGameEvent(Message);
