using System;
using System.Collections.Generic;
using System.Linq;
using Sacred.Core.Pak.Weapon;

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

    public SacredGameActor Clone()
    {
        var clone = new SacredGameActor(CharacterClass);
        for (var index = 0; index < EquipmentSlots.Count; index++)
        {
            if (EquipmentSlots[index].Equipment is { } equipment)
                clone.EquipmentSlots[index].Equip(equipment);
        }

        return clone;
    }

    /// <summary>Replaces the slots occupied by a set while preserving every other slot.</summary>
    public int EquipSet(IEnumerable<SacredEquipment> equipment)
    {
        var equipped = 0;
        foreach (var group in equipment.GroupBy(static item => EquipmentSlotRules.GetSlotType(item.EquipmentType)))
        {
            var slots = EquipmentSlots
                .Where(slot => EquipmentSlotRules.Accepts(slot.Type, group.Key))
                .ToArray();
            var slotIndex = 0;
            foreach (var item in group)
            {
                if (slotIndex >= slots.Length)
                    break;

                slots[slotIndex++].Equip(item);
                equipped++;
            }
        }

        return equipped;
    }
}

/// <summary>Maps Weapon.pak equipment categories to the playable actor's inventory slots.</summary>
public static class EquipmentSlotRules
{
    public static bool CanEquip(SacredCharacterClass characterClass, SacredEquipment equipment)
    {
        var allowedClasses = equipment.EffectiveCharacterClassMask;
        return allowedClasses == SacredCharacterClassMask.None ||
               allowedClasses.HasFlag(characterClass.ToMask());
    }

    public static EquipmentSlotType GetSlotType(SacredEquipmentType equipmentType) => equipmentType switch
    {
        SacredEquipmentType.HeadArmor => EquipmentSlotType.Head,
        SacredEquipmentType.ChestArmor => EquipmentSlotType.Body,
        SacredEquipmentType.ArmArmor => EquipmentSlotType.Arms,
        SacredEquipmentType.Gloves => EquipmentSlotType.Hands,
        SacredEquipmentType.LegArmor => EquipmentSlotType.Legs,
        SacredEquipmentType.FootArmor => EquipmentSlotType.Feet,
        SacredEquipmentType.Belt => EquipmentSlotType.Belt,
        SacredEquipmentType.Shoulder => EquipmentSlotType.Shoulder,
        SacredEquipmentType.Wings => EquipmentSlotType.Wings,
        SacredEquipmentType.Amulet => EquipmentSlotType.Amulet,
        SacredEquipmentType.Ring => EquipmentSlotType.Ring,
        SacredEquipmentType.Shield => EquipmentSlotType.LeftHand,
        _ => EquipmentSlotType.RightHand
    };

    public static bool Accepts(EquipmentSlotType slotType, EquipmentSlotType requestedType) =>
        slotType == requestedType ||
        requestedType == EquipmentSlotType.Belt && slotType == EquipmentSlotType.SmallBelt;
}

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
            _ => []
        });
        return slots;
    }

    private static void Add(List<EquipmentSlot> slots, EquipmentSlotType[] types)
    {
        foreach (var type in types) slots.Add(new EquipmentSlot(type));
    }
}
