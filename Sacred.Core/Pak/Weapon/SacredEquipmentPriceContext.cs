using Sacred.Core.Pak.Items;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Resolved native services for ordinary item pricing without merged socket records.
/// Resource-specific requirement flags must come from the installed instruction definitions.</summary>
public readonly record struct SacredEquipmentPriceContext(SacredItemCategory Category, bool HasModifier, int Modifier,
    byte SetType, bool ClearClassRestrictions = false, bool ClearLevelAndSkillRequirements = false);
