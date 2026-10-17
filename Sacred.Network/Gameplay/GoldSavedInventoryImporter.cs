using System.Buffers.Binary;
using System.Collections.Frozen;
using Sacred.Core.Pax.Hero.Inventory;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x5DE8E0 inventory fix for fully resolved objects and a unique visible grid.
/// Native missing-object/duplicate deletion is deliberately not modeled by this importer.</summary>
public static class GoldSavedInventoryImporter
{
    public const uint ObjectRemovalFlag = 0x10;

    public static GoldInventoryImportResult Import(SacredSavedInventory source, IReadOnlyDictionary<uint, uint> objectResources)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(objectResources);
        if (!source.RemainingData.IsEmpty) throw new InvalidDataException("Inventory import requires a fully accounted stream.");
        var visibleCells = (uint)source.Width * source.Height;
        if (visibleCells > SacredSavedInventoryLayout.CellCount)
            throw new InvalidDataException("Inventory grid exceeds its native 256-cell storage.");
        var visibleReferences = new HashSet<uint>();
        var occupied = new HashSet<uint>();
        var payload = source.ToPayload();
        var changed = 0;
        for (var index = 0; index < SacredSavedInventoryLayout.CellCount; index++)
        {
            var cell = source.GetCell(index);
            if (cell.ObjectReference == 0) continue;
            if (!objectResources.TryGetValue(cell.ObjectReference, out var resource))
                throw new InvalidDataException("Inventory contains an unresolved object; native removal requires object-manager integration.");
            if ((uint)index < visibleCells && !visibleReferences.Add(cell.ObjectReference))
                throw new InvalidDataException("Inventory has a duplicate visible reference; native deletion requires object-manager integration.");
            var cachedType = unchecked((ushort)resource);
            if (cachedType != cell.CachedItemType) changed++;
            var offset = SacredSavedInventoryLayout.CellsOffset + index * SacredSavedInventoryLayout.CellSize + SacredSavedInventoryCellLayout.CachedItemTypeOffset;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset), cachedType);
            occupied.Add(cell.ObjectReference);
        }
        for (var index = 0; index < SacredSavedInventoryLayout.ItemBarCount; index++)
        {
            var reference = BinaryPrimitives.ReadUInt32LittleEndian(source.GetItemBarRecord(index));
            if (reference != 0 && !objectResources.ContainsKey(reference))
                throw new InvalidDataException("Item bar contains an unresolved object; native cache removal requires object-manager integration.");
        }
        return new(SacredSavedInventory.FromPayload(payload), occupied.ToFrozenSet(), changed);
    }
}
