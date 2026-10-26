namespace Sacred.Core.Pak.Weapon;

public readonly record struct SacredEquipmentResourceRange(uint Minimum, uint Maximum)
{
    public bool Contains(uint type) => type >= Minimum && type <= Maximum;
}

public readonly record struct SacredEquipmentClassNameRule(string Text, uint Flag);

/// <summary>Original resource gates and ordered, case-sensitive name rules.
/// All resource numbers and name strings are supplied by original definitions.</summary>
public sealed record SacredEquipmentClassRules(
    IReadOnlyList<SacredEquipmentResourceRange> ExcludedResources,
    IReadOnlyList<SacredEquipmentResourceRange> ParentResources,
    int MaximumParentLinks,
    IReadOnlySet<uint> PreservedResources,
    uint ClearMask,
    string ArmorMarker,
    IReadOnlyList<string> ArmorExclusions,
    IReadOnlyList<SacredEquipmentClassNameRule> ArmorRules,
    IReadOnlyList<string> JewelryMarkers,
    IReadOnlyList<SacredEquipmentClassNameRule> JewelryRules);
