using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldEquipmentRemovalCodec
{
    public static GoldEquipmentRemovalEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != GoldEquipmentRemovalLayout.Opcode || message.Payload.Length != GoldEquipmentRemovalLayout.Size)
            throw new InvalidDataException("Gold equipment removal requires opcode 0x11D and exactly twelve payload bytes.");
        var bytes = message.Payload.AsSpan();
        return new(message, BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]));
    }

    /// <summary>Preserves the raw slot word; native handlers ignore out-of-range slots.</summary>
    public static GoldMessage Create(byte sender, byte origin, uint creatureReference, uint slot, uint expectedItemReference)
    {
        var bytes = new byte[GoldEquipmentRemovalLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, creatureReference);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), slot);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), expectedItemReference);
        return new(GoldEquipmentRemovalLayout.Opcode, sender, origin, 0, bytes);
    }
}
