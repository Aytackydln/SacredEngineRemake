namespace Sacred.Core.Pak.Weapon.Bonuses;

/// <summary>Decoded authored bonus data. Resource lookup and display formatting are separate.</summary>
public readonly record struct SacredEquipmentBonus(
    ushort Code,
    short Value,
    SacredEquipmentBonusKind Kind,
    SacredEquipmentBonusUnit Unit,
    byte AttributeSelector,
    byte HeroClass,
    byte OpponentClass,
    SacredEquipmentElement Element,
    SacredEquipmentDamageKind DamageKind);
