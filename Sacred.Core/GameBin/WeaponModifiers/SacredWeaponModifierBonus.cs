namespace Sacred.Core.GameBin.WeaponModifiers;

public readonly record struct SacredWeaponModifierBonus(
    ushort Chance,
    ushort Type,
    uint Group,
    short Minimum,
    short Maximum,
    uint Pool,
    int CombatArtLevelDivisor,
    int SkillLevelDivisor);
