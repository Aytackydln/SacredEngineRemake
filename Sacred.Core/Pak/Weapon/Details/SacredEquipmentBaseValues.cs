namespace Sacred.Core.Pak.Weapon.Details;

public readonly record struct SacredEquipmentBaseValues(
    SacredEquipmentDamage Damage,
    SacredEquipmentBaseStats Stats,
    int MovementBonus)
{
    public int MinimumDamage => Damage.PhysicalDamageMinimum + Damage.FireDamageMinimum
        + Damage.MagicDamageMinimum + Damage.PoisonDamageMinimum;

    public int MaximumDamage => Damage.PhysicalDamageMaximum + Damage.FireDamageMaximum
        + Damage.MagicDamageMaximum + Damage.PoisonDamageMaximum;

    public bool HasDamage => MinimumDamage != 0 || MaximumDamage != 0;

    public bool HasProtection => Stats.PhysicalProtection != 0 || Stats.FireProtection != 0
        || Stats.MagicProtection != 0 || Stats.PoisonProtection != 0;
}
