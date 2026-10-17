using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldInventoryCodec
{
    public const ushort Opcode = 0x122;

    /// <summary>Outer ID is supplied separately, as in the native producer. No reference remapping is performed.</summary>
    public static GoldMessage Create(byte playerSlot, uint inventoryId, SacredSavedInventory inventory)
    {
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        ArgumentNullException.ThrowIfNull(inventory);
        if (!inventory.RemainingData.IsEmpty) throw new ArgumentException("Inventory contains unparsed trailing bytes.", nameof(inventory));
        var stream = inventory.ToPayload();
        var payload = new byte[4 + stream.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, inventoryId);
        stream.CopyTo(payload, 4);
        return new(Opcode, playerSlot, playerSlot, 0, payload);
    }
}
