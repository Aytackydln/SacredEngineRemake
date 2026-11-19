using System.Buffers.Binary;
using Sacred.Core.Pak.Items;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Equipment;

/// <summary>Current normalized saved item 0x16E. Reuses physical file-field
/// projection for fields unchanged since restoration; runtime identity/flags,
/// direction, normalized values and inventory membership replace saved state.</summary>
public static class WorldEquipmentSavedSerialization
{
    public static GoldItemState Write(WorldEquipmentInitialItem item, ItemsPakEntry descriptor)
    {
        if (!item.Node.Active || !item.SavedPostLoadCompleted || item.SavedState is not { } saved || saved.Source.Count != 0 ||
            item.Generated || descriptor.ItemIndex != item.Physical.Base.ObjectType ||
            saved.Source.HasWeaponExtension != (descriptor.ModelDesc.Category == SacredItemCategory.Weapon) ||
            (item.Physical.Base.Flags & (2 | GoldPhysicalStateLayout.ModelLoadedFlag | GoldPhysicalStateLayout.VectorCCFlag)) != 2 ||
            (item.Physical.PhysicalFlags & 4) == 0)
            throw new NotSupportedException("Saved item export requires its current normalized unplaced/unloaded ordinary physical graph.");
        var bytes = GoldSavedPhysicalStateImporter.Import(saved.Source.PhysicalData).ToPayload();
        (item.Physical.Base with { Flags = item.Physical.Base.Flags & ~0x10000u }).ToPayload().CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldPhysicalStateLayout.WordB0Offset), item.Physical.PhysicalFlags);
        var direction = item.Physical.Direction;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(GoldPhysicalStateLayout.Vector70Offset), direction.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(GoldPhysicalStateLayout.Vector70Offset + 4), direction.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(GoldPhysicalStateLayout.Vector70Offset + 8), direction.Z);
        return GoldItemState.Create(GoldPhysicalState.FromPayload(bytes), [], item.Values, item.InventoryId, saved.Source.WeaponExtension);
    }
}
