namespace Sacred.Core.GameBin.WeaponModifiers;

public sealed record SacredWeaponModifier(
    int RecordIndex,
    long FileOffset,
    IReadOnlyList<uint> ItemIds,
    SacredWeaponModifierLayout Definition,
    IReadOnlyList<SacredWeaponModifierBonus> Bonuses);
