using System.Collections.Frozen;
using Sacred.Core.Pax.Hero.Inventory;

namespace Sacred.Network.Gameplay;

/// <summary>Native 0x5DD8C0 binds visible grid objects to the imported inventory's ID before packet export.</summary>
public static class GoldSavedInventoryMembership
{
    public static IReadOnlyDictionary<uint, ushort> Create(params SacredSavedInventory[] inventories)
    {
        ArgumentNullException.ThrowIfNull(inventories);
        var result = new Dictionary<uint, ushort>();
        foreach (var inventory in inventories)
        {
            ArgumentNullException.ThrowIfNull(inventory);
            var cells = checked((int)inventory.Width * inventory.Height);
            if (cells > SacredSavedInventoryLayout.CellCount) throw new InvalidDataException("Inventory grid exceeds its native 256-cell storage.");
            for (var i = 0; i < cells; i++)
            {
                var reference = inventory.GetCell(i).ObjectReference;
                if (reference != 0) result[reference] = inventory.InventoryId;
            }
        }
        // Native binding visits only the visible grid. Item-bar-only refs and cells outside it retain their existing membership.
        return result.ToFrozenDictionary();
    }
}
