namespace Sacred.Core.Pak.Weapon.Names;

/// <summary>Original executable name fragments and subtype/usage tables.
/// Subtype numbers are wpmod WeaponType values, not item resource identifiers.</summary>
public sealed record SacredEquipmentNameRules(string Prefix, IReadOnlyList<string> RarityPrefixes,
    IReadOnlyList<string> ElementPrefixes, IReadOnlyList<string> Families, IReadOnlyList<string> DefaultKeys,
    IReadOnlyList<int> UsageTypes, IReadOnlyList<string> SwordPatterns, IReadOnlyList<string> AxePatterns);
