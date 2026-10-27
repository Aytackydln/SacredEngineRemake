namespace Sacred.Core.Runtime;

/// <summary>Resolved native actor predicates: 0x54A570/0x54A3B0/0x54A480,
/// 0x54A470/0x54A2A0/0x54A660, main usage 0x546F80, and offhand probes.
/// Predicate names identify the skill gate without guessing a resource family.</summary>
public readonly record struct SacredCreatureSkillContext(bool Skill3Weapon, bool Skill4Weapon, bool Skill5Weapon,
    bool Skill6Weapon, bool Skill7Weapon, bool Skill9AlternateCurve, ushort MainUsage,
    bool OffhandPresent, bool OffhandWeaponUsage10);
