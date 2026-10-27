using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Balance;

/// <summary>Passive skill curves in Balance.bin's counted global block. Each
/// curve stores base/divisor/limit; Gold 0x566070 applies learned+bonus ranks.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredCreatureSkillBalanceLayout
{
    public const int Size = 0x1FC;
    [FieldOffset(0x40)] public readonly SacredSkillCurveLayout WeaponLoreDamage;
    [FieldOffset(0x4C)] public readonly SacredSkillCurveLayout SpellDamage;
    [FieldOffset(0x58)] public readonly SacredSkillCurveLayout UnarmedAttack;
    [FieldOffset(0x64)] public readonly SacredSkillCurveLayout UnarmedSpeed;
    [FieldOffset(0x70)] public readonly SacredSkillCurveLayout Skill4Attack;
    [FieldOffset(0x7C)] public readonly SacredSkillCurveLayout Skill4Speed;
    [FieldOffset(0x88)] public readonly SacredSkillCurveLayout Skill5Attack;
    [FieldOffset(0x94)] public readonly SacredSkillCurveLayout Skill5Speed;
    [FieldOffset(0xA0)] public readonly SacredSkillCurveLayout Skill13Attack;
    [FieldOffset(0xAC)] public readonly SacredSkillCurveLayout Skill13Speed;
    [FieldOffset(0xB8)] public readonly SacredSkillCurveLayout Skill3Attack;
    [FieldOffset(0xC4)] public readonly SacredSkillCurveLayout Skill3Speed;
    [FieldOffset(0xD0)] public readonly SacredSkillCurveLayout Skill6Attack;
    [FieldOffset(0xDC)] public readonly SacredSkillCurveLayout Skill6Speed;
    [FieldOffset(0xE8)] public readonly SacredSkillCurveLayout Skill7Attack;
    [FieldOffset(0xF4)] public readonly SacredSkillCurveLayout Skill7Speed;
    [FieldOffset(0x16C)] public readonly SacredSkillCurveLayout Skill12Regeneration;
    [FieldOffset(0x178)] public readonly SacredSkillCurveLayout Skill8Attack;
    [FieldOffset(0x184)] public readonly SacredSkillCurveLayout Skill8Defense;
    [FieldOffset(0x190)] public readonly SacredSkillCurveLayout ConstitutionRegeneration;
    [FieldOffset(0x19C)] public readonly SacredSkillCurveLayout ConstitutionHealth;
    [FieldOffset(0x1A8)] public readonly SacredSkillCurveLayout Skill9Defense;
    [FieldOffset(0x1B4)] public readonly SacredSkillCurveLayout Skill9AlternateDefense;
    [FieldOffset(0x1C0)] public readonly SacredSkillCurveLayout ArmorLorePhysical;
    [FieldOffset(0x1CC)] public readonly SacredSkillCurveLayout ArmorLoreFire;
    [FieldOffset(0x1D8)] public readonly SacredSkillCurveLayout ArmorLoreMagic;
    [FieldOffset(0x1E4)] public readonly SacredSkillCurveLayout ArmorLorePoison;
    [FieldOffset(0x1F0)] public readonly SacredSkillCurveLayout ArmorLoreMovement;
}
