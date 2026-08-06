using System;
using System.Collections.Generic;

namespace Sacred.Inventory.Actors;

public sealed class SacredGameActor
{
    public SacredGameActor(SacredCharacterClass characterClass)
    {
        CharacterClass = characterClass;
        EquipmentSlots = EquipmentSlotLayout.Create(characterClass);
    }

    public SacredCharacterClass CharacterClass { get; }
    public List<EquipmentSlot> EquipmentSlots { get; }
}

public enum SacredCharacterClass
{
    Gladiator, Seraphim, Daemon, Dwarf, WoodElf, BattleMage, DarkElf, Vampiress
}

internal static class EquipmentSlotLayout
{
    public static List<EquipmentSlot> Create(SacredCharacterClass characterClass)
    {
        var slots = new List<EquipmentSlot> { new(EquipmentSlotType.LeftHand), new(EquipmentSlotType.RightHand) };
        Add(slots, characterClass switch
        {
            SacredCharacterClass.Gladiator => [EquipmentSlotType.Head, EquipmentSlotType.Shoulder, EquipmentSlotType.Arms, EquipmentSlotType.Hands, EquipmentSlotType.Body, EquipmentSlotType.Amulet, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Feet, EquipmentSlotType.Legs, EquipmentSlotType.Belt],
            SacredCharacterClass.Seraphim => [EquipmentSlotType.Head, EquipmentSlotType.Shoulder, EquipmentSlotType.Body, EquipmentSlotType.Wings, EquipmentSlotType.Amulet, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Arms, EquipmentSlotType.Feet, EquipmentSlotType.Belt],
            SacredCharacterClass.Daemon => [EquipmentSlotType.Head, EquipmentSlotType.Shoulder, EquipmentSlotType.Arms, EquipmentSlotType.Hands, EquipmentSlotType.Body, EquipmentSlotType.Amulet, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Feet, EquipmentSlotType.Legs, EquipmentSlotType.SmallBelt],
            SacredCharacterClass.Dwarf => [EquipmentSlotType.Head, EquipmentSlotType.Cannon, EquipmentSlotType.Shoulder, EquipmentSlotType.Hands, EquipmentSlotType.Body, EquipmentSlotType.Amulet, EquipmentSlotType.Amulet, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Feet, EquipmentSlotType.SmallBelt],
            SacredCharacterClass.WoodElf or SacredCharacterClass.BattleMage => [EquipmentSlotType.Head, EquipmentSlotType.Arms, EquipmentSlotType.Hands, EquipmentSlotType.Body, EquipmentSlotType.Amulet, EquipmentSlotType.Amulet, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Feet, EquipmentSlotType.Legs, EquipmentSlotType.SmallBelt],
            SacredCharacterClass.DarkElf => [EquipmentSlotType.Head, EquipmentSlotType.Shoulder, EquipmentSlotType.Arms, EquipmentSlotType.Hands, EquipmentSlotType.Body, EquipmentSlotType.Amulet, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Feet, EquipmentSlotType.Legs, EquipmentSlotType.SmallBelt],
            SacredCharacterClass.Vampiress => [EquipmentSlotType.Head, EquipmentSlotType.Shoulder, EquipmentSlotType.Arms, EquipmentSlotType.Hands, EquipmentSlotType.Body, EquipmentSlotType.Amulet, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Ring, EquipmentSlotType.Feet, EquipmentSlotType.Legs, EquipmentSlotType.Belt],
            _ => throw new ArgumentOutOfRangeException(nameof(characterClass))
        });
        return slots;
    }

    private static void Add(List<EquipmentSlot> slots, EquipmentSlotType[] types)
    {
        foreach (var type in types) slots.Add(new EquipmentSlot(type));
    }
}
