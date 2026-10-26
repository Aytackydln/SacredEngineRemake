using Sacred.Core.Pak.Items;

namespace Sacred.Core.Pak.Weapon;

public readonly record struct SacredEquipmentSelectionPoolRules(uint MaximumResourceExclusive,
    uint FirstExcludedMinimum, uint FirstExcludedMaximum, uint SecondExcludedMinimum, uint SecondExcludedMaximum)
{
    public bool Admits(uint type) => type > 0 && type < MaximumResourceExclusive &&
        !(type >= FirstExcludedMinimum && type <= FirstExcludedMaximum) &&
        !(type >= SecondExcludedMinimum && type <= SecondExcludedMaximum);
}

public readonly record struct SacredEquipmentSelectionPoolEntry(ItemsPakEntryModelDescLayout Descriptor,
    SacredEquipmentSelectionCandidate Candidate);

/// <summary>Gold 0x5C8051..0x5C813B original category-pool membership.
/// Supply descriptors after native base-visual resolution and equipment records
/// with their native modifier associations. Native caches this initial membership;
/// build it before shared pricing/class mutations. Construction consumes no RNG.</summary>
public static class SacredEquipmentSelectionPool
{
    public static IReadOnlyList<SacredEquipmentSelectionCandidate> Build(
        IEnumerable<SacredEquipmentSelectionPoolEntry> entries, SacredItemCategory category,
        SacredEquipmentSelectionPoolRules rules)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.Where(entry => rules.Admits(entry.Candidate.ItemType) && entry.Descriptor.Category == category &&
            !entry.Descriptor.IsEquipmentFallback && !entry.Descriptor.ExcludedFromRandomEquipmentPool &&
            (category != SacredItemCategory.Rune || entry.Descriptor.UpgradeItemType != 0) &&
            entry.Candidate.Modifier?.WeaponType != -1 && (entry.Candidate.Values.Flags & 0x40) == 0)
            .OrderBy(entry => entry.Candidate.ItemType).Select(entry => entry.Candidate).ToArray();
    }
}
