using Sacred.Core.GameBin.EquipmentVariants;
using Sacred.Core.Pak.Items;

namespace Sacred.Core.Pak.Weapon;

public readonly record struct SacredEquipmentVariantResult(bool Supported, uint ReplacementType);

/// <summary>Gold 0x43CA20 after source-type canonicalization. The caller supplies
/// an already canonical source (the selector's two small class probes are unchanged).
/// No replacement is distinct from a missing profile/source association.</summary>
public static class SacredEquipmentVariantLookup
{
    public static SacredEquipmentVariantResult ResolveCanonicalSource(uint itemType, uint sourceType,
        uint minimumReplacementSource, SacredEquipmentClassRules parentRules,
        IReadOnlyList<IReadOnlyList<SacredEquipmentVariantLayout>> profiles,
        Func<uint, uint?> parentResource, Func<uint, ItemsPakEntryModelDescLayout?> descriptor)
    {
        for (var i = 0; i < parentRules.MaximumParentLinks && parentRules.ParentResources.Any(range => range.Contains(itemType)); i++)
        {
            var parent = parentResource(itemType);
            if (!parent.HasValue) { itemType = 0; break; }
            itemType = parent.Value;
        }
        var item = descriptor(itemType);
        if (item is null || item.Value.Category is SacredItemCategory.Ring or SacredItemCategory.Amulet) return new(true, 0);
        var profile = item.Value.EquipmentVariantProfile;
        if (profile == 0) return new(true, 0);
        if (profile >= profiles.Count) return new(false, 0);
        foreach (var row in profiles[profile])
            if (row.SourceType == sourceType) return new(true, sourceType < minimumReplacementSource ? 0 : row.ReplacementType);
        return new(false, 0);
    }
}
