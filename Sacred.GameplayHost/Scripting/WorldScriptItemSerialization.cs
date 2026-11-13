using System.Buffers.Binary;
using Sacred.Core.Pak.Items;
using Sacred.GameplayHost.Equipment;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Initial QuestItem/Book/Weapon/Shield 0x16E in the unloaded direct-placement caller.
/// The original zero-handle bone query's incidental stack vector is rejected by
/// the later position consumer at these logical coordinates. Only that compared
/// path canonicalizes the cache; placed non-creature caches are not all ignored.</summary>
public static class WorldScriptItemSerialization
{
    public static GoldItemState Write(WorldEquipmentInitialItem item, ItemsPakEntry descriptor)
    {
        var physical = item.Physical;
        var state = physical.Base;
        if (descriptor.ItemIndex != state.ObjectType || descriptor.ModelDesc.Category is not (SacredItemCategory.QuestItem or SacredItemCategory.Book or SacredItemCategory.Weapon or SacredItemCategory.Shield) ||
            !physical.Item || !physical.Physical || state.ObjectReference <= 16 || state.Surface != 0 ||
            physical.AttachmentParent != 0 || physical.PhysicalFlags != 0 ||
            (state.Flags & (2 | GoldPhysicalStateLayout.ModelLoadedFlag | GoldPhysicalStateLayout.VectorCCFlag)) != 0)
            throw new NotSupportedException("Startup item writing requires its compared unloaded, unattached, directly placed item.");
        // Native 0x44D612..0x44D64E accepts some non-creature cache positions
        // below 1000 logical units. At/above 1000 every branch recomputes the
        // observed near-zero constructor stack cache from the logical position.
        if (Math.Max(Math.Abs((long)state.X), Math.Abs((long)state.Y)) < 1000)
            throw new NotSupportedException("Near-origin initial items need their native bone/cache initialization before writing.");
        var prefix = GoldPhysicalConstructionWriter.Write(state, physical.Direction);
        // cWeapon's four extension words are constructor defaults, independent
        // of authored instance values. The compared Shield uses the item trailer.
        var extension = descriptor.ModelDesc.Category == SacredItemCategory.Weapon
            ? new byte[GoldItemStateLayout.WeaponExtensionSize] : [];
        if (extension.Length != 0) BinaryPrimitives.WriteUInt32LittleEndian(extension, GoldItemStateLayout.WeaponMarker);
        return GoldItemState.Create(prefix, [], item.Values, item.InventoryId, extension);
    }
}
