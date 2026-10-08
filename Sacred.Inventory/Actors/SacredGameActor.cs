using System;
using System.Collections.Generic;
using System.Linq;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Inventory.Items;
using Sacred.Inventory.Stats;

namespace Sacred.Inventory.Actors;

public sealed class SacredGameActor
{
    public SacredGameActor(SacredCharacterClass characterClass)
        : this(characterClass, null)
    {
    }

    public SacredGameActor(SacredCharacterClass characterClass, SacredCreatureTemplate? template)
    {
        CharacterClass = characterClass;
        EquipmentSlots = EquipmentSlotLayout.Create(characterClass);
        Progression = new(template);
        ObserveChanges();
    }

    public SacredGameActor(SacredCreatureTemplate? template, IEnumerable<EquipmentSlotType> slotTypes)
    {
        EquipmentSlots = slotTypes.Select(type => new EquipmentSlot(type)).ToList();
        Progression = new(template);
        ObserveChanges();
    }

    public SacredCharacterClass CharacterClass { get; private init; }
    public List<EquipmentSlot> EquipmentSlots { get; }
    public SacredCharacterProgression Progression { get; }
    public SacredActorStats InherentStats { get; private set; } = SacredActorStats.Empty;
    public SacredActorStats EquipmentStats { get; private set; } = SacredActorStats.Empty;
    public SacredActorStats TotalStats { get; private set; } = SacredActorStats.Empty;
    public IReadOnlyList<SacredBonusTotal> BonusSummary { get; private set; } = [];
    public event Action? StatsChanged;

    private void ObserveChanges()
    {
        Progression.Changed += RecalculateStats;
        foreach (var slot in EquipmentSlots) slot.Changed += RecalculateStats;
        RecalculateStats();
    }

    public void RecalculateStats()
    {
        var items = EquipmentSlots.Select(slot => slot.Instance).OfType<SacredItemInstance>()
            .DistinctBy(item => item.InstanceId).ToArray();
        InherentStats = Progression.CreateStats();
        var template = Progression.Template;
        var hero = template?.Class == SacredCreatureClass.Hero && template.ItemType <= byte.MaxValue ? (byte)template.ItemType : (byte)0;
        EquipmentStats = SacredEquipmentStatsCalculator.Calculate(items, InherentStats, hero, Progression);
        TotalStats = InherentStats + EquipmentStats;
        BonusSummary = SacredEquipmentStatsCalculator.Summarize(items);
        StatsChanged?.Invoke();
    }

    /// <summary>Moves a concrete item between slots; a two-handed item replaces both hands.</summary>
    public void Equip(int slotIndex, SacredItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var target = EquipmentSlots[slotIndex];
        if (!EquipmentSlotRules.AcceptsItem(target.Type, item.Definition))
            throw new ArgumentException($"This item cannot occupy {target.Type}.", nameof(item));
        foreach (var slot in EquipmentSlots.Where(slot => slot != target))
        {
            if (slot.Instance?.InstanceId == item.InstanceId ||
                target.Type is EquipmentSlotType.LeftHand or EquipmentSlotType.RightHand &&
                slot.Type is EquipmentSlotType.LeftHand or EquipmentSlotType.RightHand &&
                (item.Definition.InferredTwoHanded == true || slot.Equipment?.InferredTwoHanded == true))
                slot.Unequip();
        }
        target.Equip(item);
    }

    public SacredGameActor Clone()
    {
        var clone = new SacredGameActor(Progression.Template, EquipmentSlots.Select(slot => slot.Type)) { CharacterClass = CharacterClass };
        Progression.CopyTo(clone.Progression);
        for (var index = 0; index < EquipmentSlots.Count; index++)
        {
            if (EquipmentSlots[index].Instance is { } item)
                clone.EquipmentSlots[index].Equip(item);
        }

        return clone;
    }

    /// <summary>Replaces the slots occupied by a set while preserving every other slot.</summary>
    public int EquipSet(IEnumerable<SacredEquipment> equipment)
    {
        var equipped = 0;
        foreach (var group in equipment.Where(item => EquipmentSlots.Any(slot => EquipmentSlotRules.AcceptsItem(slot.Type, item)))
                     .GroupBy(static item => EquipmentSlotRules.GetSlotType(item)))
        {
            var slots = EquipmentSlots
                .Where(slot => EquipmentSlotRules.Accepts(slot.Type, group.Key))
                .ToArray();
            var slotIndex = 0;
            foreach (var item in group)
            {
                if (slotIndex >= slots.Length)
                    break;

                Equip(EquipmentSlots.IndexOf(slots[slotIndex++]), SacredItemInstance.FromDefinition(item));
                equipped++;
            }
        }

        return equipped;
    }
}

/// <summary>Maps Items.pak families and Weapon.pak usage codes to actor inventory slots.</summary>
public static class EquipmentSlotRules
{
    public static bool AcceptsItem(EquipmentSlotType slot, SacredEquipment item)
    {
        var category = item.Item.ModelDesc.Category;
        if (category == SacredItemCategory.Weapon)
        {
            // Gold TypeManager::slotAcceptsItem (0x43EFC0): bows, crossbows and
            // muskets occupy the left slot; other weapons occupy the right slot.
            var rangedLeft = IsLeftHandWeapon(item);
            return slot == (rangedLeft ? EquipmentSlotType.LeftHand : EquipmentSlotType.RightHand);
        }
        if (category is not (SacredItemCategory.Shield or SacredItemCategory.ChestArmor or
            SacredItemCategory.Helmet or SacredItemCategory.ShoulderArmor or SacredItemCategory.ArmArmor or
            SacredItemCategory.Gloves or SacredItemCategory.LegArmor or SacredItemCategory.FootArmor or
            SacredItemCategory.Belt or SacredItemCategory.Wings or SacredItemCategory.Amulet or
            SacredItemCategory.Ring or SacredItemCategory.DwarfCannon)) return false;
        return Accepts(slot, GetSlotType(item));
    }

    public static EquipmentSlotType GetSlotType(SacredEquipment item) =>
        item.Item.ModelDesc.Category == SacredItemCategory.Weapon && IsLeftHandWeapon(item)
            ? EquipmentSlotType.LeftHand : GetSlotType(item.EquipmentType);

    // Gold isBow (0x427E30): 7/13; isCrossBow (0x427E80): 12/14.
    private static bool IsLeftHandWeapon(SacredEquipment item) => item.UsageIdentifier is 7 or 13 or 12 or 14;

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
        SacredEquipmentType.DwarfCannon => EquipmentSlotType.Cannon,
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
