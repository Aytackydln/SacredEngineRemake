using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x7DF01A: item reference and actor facing bits. Only 0x142 removes the ground anchor.</summary>
public static class GoldItemPickupCodec
{
    public const ushort RequestOpcode = 0x141;
    public const ushort TakenOpcode = 0x142;
    public const int Size = 8;
    /// <summary>Gold 0x53CACC emits the item's reference and cObject3D::getDirection result.</summary>
    public static GoldMessage Create(byte playerSlot, uint objectReference, uint facingBits)
    {
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        var bytes = new byte[Size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, objectReference);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), facingBits);
        return new(RequestOpcode, playerSlot, playerSlot, 0, bytes);
    }
    public static GoldItemPickupEvent Decode(GoldMessage message)
    {
        if (message.Opcode is not (RequestOpcode or TakenOpcode)) throw new ArgumentOutOfRangeException(nameof(message));
        if (message.Payload.Length != Size) throw new InvalidDataException("Gold pickup requires exactly eight bytes.");
        return new(message with { Payload = message.Payload.ToArray() },
            BinaryPrimitives.ReadUInt32LittleEndian(message.Payload), BinaryPrimitives.ReadUInt32LittleEndian(message.Payload.AsSpan(4)));
    }
}
