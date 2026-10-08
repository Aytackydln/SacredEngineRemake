namespace Sacred.Core.Pak.Weapon;

/// <summary>Authored requirements, before item-level scaling or a character's modifiers.</summary>
public readonly record struct SacredEquipmentRequirements(
    byte Level, byte Strength, byte Dexterity, byte Charisma, byte Endurance,
    byte Skill, byte SkillLevel);

/// <summary>Signed native AW/PW/BW and protection values from Weapon.pak offsets 172–184.</summary>
public readonly record struct SacredEquipmentBaseStats(
    short Attack, short Defense, short BW,
    short PhysicalProtection, short FireProtection, short MagicProtection, short PoisonProtection)
{
    public int TotalProtection => PhysicalProtection + FireProtection + MagicProtection + PoisonProtection;
}
