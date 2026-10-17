using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldEquipmentAssignmentCodec
{
    public static GoldEquipmentAssignmentEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != GoldEquipmentAssignmentLayout.Opcode || message.Payload.Length != GoldEquipmentAssignmentLayout.Size)
            throw new InvalidDataException("Gold equipment assignment requires opcode 0x11E and exactly twelve payload bytes.");
        var bytes = message.Payload.AsSpan();
        return new(message, BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]));
    }

    /// <summary>Retains invalid raw slots so the native dispatcher guard can ignore them without truncation.</summary>
    public static GoldMessage Create(byte sender, byte origin, uint creatureReference, uint slot, uint itemReference)
    {
        var bytes = new byte[GoldEquipmentAssignmentLayout.Size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, creatureReference);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), slot);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), itemReference);
        return new(GoldEquipmentAssignmentLayout.Opcode, sender, origin, 0, bytes);
    }
}
