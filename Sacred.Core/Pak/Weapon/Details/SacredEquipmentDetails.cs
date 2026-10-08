using Sacred.Core.Pak.Weapon.Bonuses;

namespace Sacred.Core.Pak.Weapon.Details;

/// <summary>Authored equipment facts, independent of localization and tooltip presentation.</summary>
public sealed record SacredEquipmentDetails(
    SacredEquipmentIdentity Identity,
    SacredEquipmentBaseValues BaseValues,
    SacredEquipmentRequirements Requirements,
    IReadOnlyList<SacredEquipmentBonus> Bonuses);
