namespace Sacred.Core.Pak.Weapon.Descriptions;

/// <summary>Localized tooltip output. Unformatted equipment facts are exposed by SacredEquipmentDetails.</summary>
public sealed record SacredEquipmentDescription(string Name, IReadOnlyList<SacredEquipmentDescriptionSection> Sections);
