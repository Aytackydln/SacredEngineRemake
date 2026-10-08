using System.Runtime.InteropServices;

namespace Sacred.Core.Pak.Weapon;

/// <summary>Native sWeaponInfo: the 130-byte mutable item-value block. Weapon.pak embeds
/// it at +128; cItemBase owns a resolved copy and merged socket records. Demo save writes
/// this block separately from sWeaponInfoShared. These values are outcomes, not generation rules.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredEquipmentInstanceLayout
{
    public const int Size = 130;
    [FieldOffset(0)] public readonly uint ItemType;
    [FieldOffset(4)] public readonly uint Flags;
    [FieldOffset(8)] public readonly uint Price;
    [FieldOffset(12)] public readonly SacredEquipmentSlotTypes SlotTypes;
    [FieldOffset(20)] public readonly byte MinimumLevel;
    [FieldOffset(21)] public readonly byte MinimumStrength;
    [FieldOffset(22)] public readonly byte MinimumDexterity;
    [FieldOffset(23)] public readonly byte MinimumCharisma;
    [FieldOffset(24)] public readonly byte MinimumEndurance;
    /// <summary>Native SpawnLevel: the concrete level on a resolved runtime item.</summary>
    [FieldOffset(25)] public readonly byte Level;
    [FieldOffset(26)] public readonly byte MinimumSkill;
    [FieldOffset(27)] public readonly byte MinimumSkillLevel;
    [FieldOffset(28)] public readonly SacredEquipmentDamage Damage;
    [FieldOffset(44)] public readonly short Attack;
    [FieldOffset(46)] public readonly short Defense;
    [FieldOffset(48)] public readonly short BW;
    [FieldOffset(50)] public readonly short PhysicalProtection;
    [FieldOffset(52)] public readonly short FireProtection;
    [FieldOffset(54)] public readonly short MagicProtection;
    [FieldOffset(56)] public readonly short PoisonProtection;
    [FieldOffset(58)] public readonly SacredEquipmentBonusTypes BonusTypes;
    [FieldOffset(74)] public readonly SacredEquipmentBonusGroups BonusGroups;
    [FieldOffset(106)] public readonly SacredEquipmentBonusValues BonusValues;
    [FieldOffset(122)] public readonly SacredEquipmentLegacyRequirements LegacyRequirements;
    [FieldOffset(129)] public readonly byte BlacksmithLevel;
}
