using System;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Inventory.Actors;

public static class SacredCharacterClassExtensions
{
    public static SacredCharacterClassMask ToMask(this SacredCharacterClass characterClass) => characterClass switch
    {
        SacredCharacterClass.Seraphim => SacredCharacterClassMask.Seraphim,
        SacredCharacterClass.Gladiator => SacredCharacterClassMask.Gladiator,
        SacredCharacterClass.BattleMage => SacredCharacterClassMask.BattleMage,
        SacredCharacterClass.DarkElf => SacredCharacterClassMask.DarkElf,
        SacredCharacterClass.WoodElf => SacredCharacterClassMask.WoodElf,
        SacredCharacterClass.Vampiress => SacredCharacterClassMask.Vampiress,
        SacredCharacterClass.Dwarf => SacredCharacterClassMask.Dwarf,
        SacredCharacterClass.Daemon => SacredCharacterClassMask.Daemon,
        _ => throw new ArgumentOutOfRangeException(nameof(characterClass))
    };
}

public enum SacredCharacterClass
{
    Gladiator, Seraphim, Daemon, Dwarf, WoodElf, BattleMage, DarkElf, Vampiress
}
