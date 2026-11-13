using System.Buffers.Binary;
using Sacred.Core.Pak.Items;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Equipment;

/// <summary>CreateNPC's installed, unplaced item 0x16E stream. Native receive
/// discards the zero-handle bone cache under base flag 2. Weapon-only extension
/// words are constructor defaults; shield streams end at the ordinary trailer.</summary>
public static class WorldEquipmentInitialSerialization
{
    public static GoldItemState Write(WorldEquipmentInitialItem item, ItemsPakEntry descriptor)
    {
        var physical = item.Physical;
        if (descriptor.ItemIndex != physical.Base.ObjectType || descriptor.ModelDesc.Category is not (SacredItemCategory.Weapon or SacredItemCategory.Shield) ||
            !item.Generated || !physical.Item || !physical.Physical || physical.AttachmentParent <= 16 ||
            (physical.Base.Flags & (2 | GoldPhysicalStateLayout.ModelLoadedFlag | GoldPhysicalStateLayout.VectorCCFlag)) != 2)
            throw new NotSupportedException("Initial equipment serialization requires its generated unloaded unplaced weapon/shield attached to the NPC.");
        var bytes = GoldPhysicalConstructionWriter.Write(physical.Base, physical.Direction).ToPayload();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldPhysicalStateLayout.WordB0Offset), physical.PhysicalFlags);
        var extension = descriptor.ModelDesc.Category == SacredItemCategory.Weapon ? new byte[GoldItemStateLayout.WeaponExtensionSize] : [];
        if (extension.Length != 0) BinaryPrimitives.WriteUInt32LittleEndian(extension, GoldItemStateLayout.WeaponMarker);
        return GoldItemState.Create(GoldPhysicalState.FromPayload(bytes), [], item.Values, item.InventoryId, extension);
    }
}
