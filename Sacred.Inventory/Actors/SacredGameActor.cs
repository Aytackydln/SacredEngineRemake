using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Weapon;
using Sacred.Inventory.CombatArts;
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
        Inventory = new(EquipmentSlotLayout.Create(characterClass).Select(slot => slot.Type));
        Character = new(template);
        ObserveChanges();
    }

    public SacredGameActor(SacredCreatureTemplate? template, IEnumerable<EquipmentSlotType> slotTypes)
    {
        Inventory = new(slotTypes);
        Character = new(template);
        ObserveChanges();
    }

    public SacredCharacterClass CharacterClass { get; private init; }
    public SacredActorBaseStats BaseStats => Progression.BaseStats;
    public SacredActorCombatArts CombatArts => Progression.CombatArts;
    public SacredActorInventory Inventory { get; }
    public ReadOnlyCollection<EquipmentSlot> EquipmentSlots => Inventory.EquipmentSlots;
    public SacredActorCharacter Character { get; }
    public SacredCharacterProgression Progression => Character.Progression;
    public SacredActorStats InherentStats { get; private set; } = SacredActorStats.Empty;
    public SacredActorStats EquipmentStats { get; private set; } = SacredActorStats.Empty;
    public SacredActorStats TotalStats { get; private set; } = SacredActorStats.Empty;
    public IReadOnlyList<SacredBonusTotal> BonusSummary { get; private set; } = [];
    public event Action? StatsChanged;

    private void ObserveChanges()
    {
        Progression.Changed += RecalculateStats;
        Inventory.Changed += RecalculateStats;
        RecalculateStats();
    }

    public void RecalculateStats()
    {
        var items = Inventory.EquippedItems.ToArray();
        InherentStats = Progression.CreateStats();
        var template = Progression.Template;
        var hero = template?.Class == SacredCreatureClass.Hero && template.ItemType <= byte.MaxValue ? (byte)template.ItemType : (byte)0;
        EquipmentStats = SacredEquipmentStatsCalculator.Calculate(items, InherentStats, hero, Progression);
        CombatArts.ApplyEquipmentBonuses(EquipmentStats.CombatArts);
        TotalStats = InherentStats + EquipmentStats;
        BonusSummary = SacredEquipmentStatsCalculator.Summarize(items);
        StatsChanged?.Invoke();
    }

    /// <summary>Moves a concrete item between slots; a two-handed item replaces both hands.</summary>
    public void Equip(int slotIndex, SacredItemInstance item) => Inventory.Equip(slotIndex, item);

    public SacredGameActor Clone()
    {
        var clone = new SacredGameActor(Progression.Template, EquipmentSlots.Select(slot => slot.Type)) { CharacterClass = CharacterClass };
        Progression.CopyTo(clone.Progression);
        Inventory.CopyTo(clone.Inventory);
        for (var index = 0; index < EquipmentSlots.Count; index++)
            clone.EquipmentSlots[index].SetDefaultAppearance(EquipmentSlots[index].DefaultAppearance);
        Character.Health.CopyTo(clone.Character.Health);

        return clone;
    }

    /// <summary>Replaces the slots occupied by a set while preserving every other slot.</summary>
    public int EquipSet(IEnumerable<SacredEquipment> equipment)
    {
        var equipped = 0;
        foreach (var group in equipment.Where(item => !item.IsAppearanceFallback &&
                     EquipmentSlots.Any(slot => EquipmentSlotRules.AcceptsItem(slot.Type, item)))
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
