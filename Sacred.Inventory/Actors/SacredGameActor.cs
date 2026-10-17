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
    private SacredMovementCalculator? _movement;
    private IReadOnlyList<SacredItemInstance>? _nativeMovementEquipment;
    private uint _objectFlags, _valueFlags, _behaviorFlags;
    private float _movementFactor = 1;
    public SacredMovementSpeeds? Movement { get; private set; }

    /// <summary>Native CalcResults processes equipment in its 19 reference-slot order.
    /// Earlier Armor Lore bonuses affect the penalties of later records.</summary>
    public void SetNativeMovementEquipment(IEnumerable<SacredItemInstance> items)
    {
        _nativeMovementEquipment = Array.AsReadOnly(items.DistinctBy(item => item.InstanceId).ToArray());
        RecalculateStats();
    }

    public void ConfigureMovement(SacredMovementCalculator calculator)
    {
        _movement = calculator ?? throw new ArgumentNullException(nameof(calculator));
        RecalculateStats();
    }

    public void SetMovementRuntime(float factor, uint objectFlags, uint valueFlags, uint behaviorFlags = 0)
    {
        if (_movementFactor == factor && _objectFlags == objectFlags && _valueFlags == valueFlags && _behaviorFlags == behaviorFlags) return;
        (_movementFactor, _objectFlags, _valueFlags, _behaviorFlags) = (factor, objectFlags, valueFlags, behaviorFlags);
        RecalculateStats();
    }
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
        if (_movement is not null)
            InherentStats = InherentStats.WithValue(SacredActorStat.MovementSpeed, _movement.BasePercent(Progression));
        var template = Progression.Template;
        var hero = template?.Class == SacredCreatureClass.Hero && template.ItemType <= byte.MaxValue ? (byte)template.ItemType : (byte)0;
        EquipmentStats = SacredEquipmentStatsCalculator.Calculate(items, InherentStats, hero, Progression);
        CombatArts.ApplyEquipmentBonuses(EquipmentStats.CombatArts);
        TotalStats = InherentStats + EquipmentStats;
        if (_movement is not null && template?.Class == SacredCreatureClass.Hero)
        {
            Movement = _movement.Calculate(Progression, InherentStats, _nativeMovementEquipment ?? items, _movementFactor, _objectFlags, _valueFlags, _behaviorFlags);
            TotalStats = TotalStats.WithValue(SacredActorStat.MovementSpeed, Movement.Value.Percent);
        }
        BonusSummary = SacredEquipmentStatsCalculator.Summarize(items);
        StatsChanged?.Invoke();
    }

    /// <summary>Moves a concrete item between slots; a two-handed item replaces both hands.</summary>
    public void Equip(int slotIndex, SacredItemInstance item) => Inventory.Equip(slotIndex, item);

    public SacredGameActor Clone()
    {
        var clone = new SacredGameActor(Progression.Template, EquipmentSlots.Select(slot => slot.Type)) { CharacterClass = CharacterClass };
        Progression.CopyTo(clone.Progression);
        clone._movement = _movement;
        clone._nativeMovementEquipment = _nativeMovementEquipment;
        clone.SetMovementRuntime(_movementFactor, _objectFlags, _valueFlags, _behaviorFlags);
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
