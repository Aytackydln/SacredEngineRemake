using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Creature;

[InlineArray(8)] public struct CreatureBaseBytes { private byte _first; }
[InlineArray(2)] public struct CreatureStartingSkills { private byte _first; }
[InlineArray(14)] public struct CreatureDemoSkills { private byte _first; }
[InlineArray(16)] public struct CreatureGoldSkills { private byte _first; }
[InlineArray(6)] public struct CreatureBonusCodes { private ushort _first; }
[InlineArray(6)] public struct CreatureBonusBytes { private byte _first; }
[InlineArray(5)] public struct CreatureResistanceFactors { private byte _first; }

/// <summary>CIF header shared by the 2004 Demo and Gold Creature.pak.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct CreaturePakHeaderLayout
{
    public const int Size = 256;
    [FieldOffset(0)] public readonly uint Signature;
    [FieldOffset(4)] public readonly uint Count;
    [FieldOffset(8), BinaryUnknown] public readonly uint Revision;
}

/// <summary>2004 sHeroEntry, read as 0x40 bytes by Demo 0x4213E0.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct CreatureDemoRecordLayout
{
    public const int Size = 64;
    [FieldOffset(0)] public readonly uint ItemType;
    [FieldOffset(4)] public readonly SacredCreatureClass Class;
    [FieldOffset(5)] public readonly byte HorseClass;
    [FieldOffset(6)] public readonly ushort Flags;
    [FieldOffset(8)] public readonly ushort ExperienceMinimum;
    [FieldOffset(10)] public readonly ushort ExperienceMaximum;
    /// <summary>ST, GS, RP, WI, CH, RM, reserved, remaining attribute points.
    /// Native initialization reorders these into ST, WI, GS, RP, RM, CH.</summary>
    [FieldOffset(12)] public readonly CreatureBaseBytes Base;
    [FieldOffset(12)] public readonly byte Strength;
    [FieldOffset(13)] public readonly byte Dexterity;
    [FieldOffset(14)] public readonly byte PhysicalRegeneration;
    [FieldOffset(15)] public readonly byte Endurance;
    [FieldOffset(16)] public readonly byte Charisma;
    [FieldOffset(17)] public readonly byte MentalRegeneration;
    [FieldOffset(18), BinaryUnknown] public readonly byte ReservedBase;
    [FieldOffset(19)] public readonly byte RemainingAttributePoints;
    [FieldOffset(20)] public readonly CreatureStartingSkills StartingSkills;
    [FieldOffset(22)] public readonly CreatureDemoSkills AvailableSkills;
    [FieldOffset(36)] public readonly ushort WalkSpeed;
    [FieldOffset(38)] public readonly ushort RunSpeed;
    /// <summary>Six native innate BonusAuf selectors, not Weapon.pak bonus codes.</summary>
    [FieldOffset(40)] public readonly CreatureBonusCodes BonusCodes;
    [FieldOffset(52)] public readonly CreatureBonusBytes BonusPercent;
    [FieldOffset(58)] public readonly CreatureBonusBytes BonusClasses;
}

/// <summary>Gold sHeroEntry, read as 0x56 bytes by 0x4371C0.
/// SKILLSX grows to 16 entries; native RF/RM/RG parsing supplies resistance curves.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct CreatureGoldRecordLayout
{
    public const int Size = 86;
    [FieldOffset(0)] public readonly uint ItemType;
    [FieldOffset(4)] public readonly SacredCreatureClass Class;
    [FieldOffset(5)] public readonly byte HorseClass;
    [FieldOffset(6)] public readonly ushort Flags;
    [FieldOffset(8)] public readonly ushort ExperienceMinimum;
    [FieldOffset(10)] public readonly ushort ExperienceMaximum;
    [FieldOffset(12)] public readonly CreatureBaseBytes Base;
    [FieldOffset(12)] public readonly byte Strength;
    [FieldOffset(13)] public readonly byte Dexterity;
    [FieldOffset(14)] public readonly byte PhysicalRegeneration;
    [FieldOffset(15)] public readonly byte Endurance;
    [FieldOffset(16)] public readonly byte Charisma;
    [FieldOffset(17)] public readonly byte MentalRegeneration;
    [FieldOffset(18), BinaryUnknown] public readonly byte ReservedBase;
    [FieldOffset(19)] public readonly byte RemainingAttributePoints;
    [FieldOffset(20)] public readonly CreatureStartingSkills StartingSkills;
    [FieldOffset(22)] public readonly CreatureGoldSkills AvailableSkills;
    [FieldOffset(38)] public readonly ushort WalkSpeed;
    [FieldOffset(40)] public readonly ushort RunSpeed;
    [FieldOffset(42)] public readonly CreatureBonusCodes BonusCodes;
    [FieldOffset(54)] public readonly CreatureBonusBytes BonusPercent;
    [FieldOffset(60)] public readonly CreatureBonusBytes BonusClasses;
    [FieldOffset(66)] public readonly CreatureResistanceFactors PhysicalResistanceFactors;
    [FieldOffset(71)] public readonly CreatureResistanceFactors FireResistanceFactors;
    [FieldOffset(76)] public readonly CreatureResistanceFactors MagicResistanceFactors;
    [FieldOffset(81)] public readonly CreatureResistanceFactors PoisonResistanceFactors;
}

public enum SacredCreatureClass : byte
{
    Unknown, Hero, Monster, Npc, Horse, Undead, Animal, Mercenary, Goblin,
    Demon, Dragon, Energy, Elf, AllEnemies, Human, Dryad
}
