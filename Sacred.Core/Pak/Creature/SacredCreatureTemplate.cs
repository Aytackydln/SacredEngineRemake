using Sacred.Core.Pak.Weapon;

namespace Sacred.Core.Pak.Creature;

/// <summary>Authored creature definition; allocations and equipment live on runtime actors.</summary>
public sealed record SacredCreatureTemplate(
    int RecordIndex, int RecordOffset, int RecordSize, uint ItemType,
    SacredCreatureClass Class, byte HorseClass, ushort Flags,
    ushort ExperienceMinimum, ushort ExperienceMaximum,
    byte Strength, byte Endurance, byte Dexterity, byte PhysicalRegeneration,
    byte MentalRegeneration, byte Charisma, byte RemainingAttributePoints,
    ushort WalkSpeed, ushort RunSpeed,
    IReadOnlyList<byte> StartingSkills, IReadOnlyList<byte> AvailableSkills,
    IReadOnlyList<SacredCreatureInnateBonus> InnateBonuses,
    IReadOnlyList<byte> ResistanceFactors)
{
    /// <summary>Playable class from the native hero type; other creature classes have no player restriction.</summary>
    public SacredCharacterClassMask PlayableClassMask => Class != SacredCreatureClass.Hero
        ? SacredCharacterClassMask.None : ItemType switch
        {
            // Gold canCast (0x4D1F30 / 0x4D28D0): types 6/7 share the Vampiress
            // caster bit; 8/9 use bits 22/23, represented here by equipment masks.
            >= 1 and <= 6 => (SacredCharacterClassMask)(1 << ((int)ItemType - 1)),
            7 => SacredCharacterClassMask.Vampiress,
            8 => SacredCharacterClassMask.Dwarf,
            9 => SacredCharacterClassMask.Daemon,
            _ => SacredCharacterClassMask.None
        };
}

/// <summary>Gold CalcResults' BonusAuf: low byte selects the source/scaling
/// operation; high byte selects damage/protection/attack/defense destination.
/// These are not equipment bonus codes. The passive result pass ignores rows
/// whose CreatureClass byte is nonzero (Gold 0x57B6F0).</summary>
public readonly record struct SacredCreatureInnateBonus(ushort Code, byte Percent, byte CreatureClass)
{
    public byte SourceOperation => unchecked((byte)Code);
    public byte Destination => (byte)(Code >> 8);
}
