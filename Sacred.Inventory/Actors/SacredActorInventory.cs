using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Sacred.Core.Runtime;
using Sacred.Inventory.Items;

namespace Sacred.Inventory.Actors;

/// <summary>Actor inventory ownership, equipment references and backpack anchors.
/// Replaced or explicitly unequipped items remain owned without a grid position.
/// This permits PoC loadouts without inventing native automatic placement rules.</summary>
public sealed class SacredActorInventory
{
    private readonly Dictionary<Guid, SacredItemInstance> _items = new();
    private int _updateDepth;

    public SacredActorInventory(IEnumerable<EquipmentSlotType> slotTypes,
        int backpackWidth = SacredInventoryEntryLayout.DefaultWidth,
        int backpackHeight = SacredInventoryEntryLayout.DefaultHeight)
    {
        EquipmentSlots = slotTypes.Select(type => new EquipmentSlot(type)).ToList().AsReadOnly();
        Backpack = new(backpackWidth, backpackHeight);
        Items = new ReadOnlyDictionary<Guid, SacredItemInstance>(_items);
        foreach (var slot in EquipmentSlots) slot.Changed += SlotChanged;
    }

    public ReadOnlyCollection<EquipmentSlot> EquipmentSlots { get; }
    public SacredBackpack Backpack { get; }
    public IReadOnlyDictionary<Guid, SacredItemInstance> Items { get; }
    public event Action? Changed;

    public IEnumerable<SacredItemInstance> EquippedItems => EquipmentSlots.Select(slot => slot.Instance)
        .OfType<SacredItemInstance>().DistinctBy(item => item.InstanceId);
    public IEnumerable<SacredItemInstance> UnplacedItems => _items.Values.Where(item =>
        !Backpack.Placements.ContainsKey(item.InstanceId) && !EquipmentSlots.Any(slot => slot.Instance?.InstanceId == item.InstanceId));

    public void Equip(int slotIndex, SacredItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Definition.IsAppearanceFallback)
            throw new ArgumentException("Appearance fallbacks cannot be inventory items.", nameof(item));
        var target = EquipmentSlots[slotIndex];
        if (!EquipmentSlotRules.AcceptsItem(target.Type, item.Definition))
            throw new ArgumentException($"This item cannot occupy {target.Type}.", nameof(item));
        EquipReceived(slotIndex, item);
    }

    /// <summary>Apply an authoritative slot, including native offhand weapon assignments.
    /// Keep ownership and two-handed exclusion without applying local slot eligibility.</summary>
    public void EquipReceived(int slotIndex, SacredItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Definition.IsAppearanceFallback)
            throw new ArgumentException("Appearance fallbacks cannot be inventory items.", nameof(item));
        var target = EquipmentSlots[slotIndex];
        Update(() =>
        {
            _items.TryAdd(item.InstanceId, item);
            Backpack.Remove(item.InstanceId);
            foreach (var slot in EquipmentSlots.Where(slot => slot != target))
                if (slot.Instance?.InstanceId == item.InstanceId ||
                    target.Type is EquipmentSlotType.LeftHand or EquipmentSlotType.RightHand &&
                    slot.Type is EquipmentSlotType.LeftHand or EquipmentSlotType.RightHand &&
                    (item.Definition.InferredTwoHanded == true || slot.Equipment?.InferredTwoHanded == true))
                    slot.Unequip();
            target.Equip(item);
        });
    }

    /// <summary>Only changes ownership/equipment after a complete placement succeeds.</summary>
    public bool TryStore(SacredItemInstance item, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Definition.IsAppearanceFallback) return false;
        if (!Backpack.TryPlace(item, x, y)) return false;
        CompleteStore(item);
        return true;
    }

    public bool TryStore(SacredItemInstance item, bool rowFirst = false)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Definition.IsAppearanceFallback) return false;
        if (!Backpack.TryPlaceFirst(item, rowFirst)) return false;
        CompleteStore(item);
        return true;
    }

    public bool Remove(Guid instanceId)
    {
        if (!_items.ContainsKey(instanceId)) return false;
        Update(() =>
        {
            foreach (var slot in EquipmentSlots.Where(slot => slot.Instance?.InstanceId == instanceId)) slot.Unequip();
            Backpack.Remove(instanceId);
            _items.Remove(instanceId);
        });
        return true;
    }

    private void CompleteStore(SacredItemInstance item) => Update(() =>
    {
        _items.TryAdd(item.InstanceId, item);
        foreach (var slot in EquipmentSlots.Where(slot => slot.Instance?.InstanceId == item.InstanceId)) slot.Unequip();
    });

    private void SlotChanged()
    {
        foreach (var item in EquippedItems)
        {
            _items.TryAdd(item.InstanceId, item);
            if (_updateDepth == 0) Backpack.Remove(item.InstanceId);
        }
        if (_updateDepth == 0) Changed?.Invoke();
    }

    private void Update(Action action)
    {
        _updateDepth++;
        try { action(); }
        finally
        {
            _updateDepth--;
            if (_updateDepth == 0) Changed?.Invoke();
        }
    }

    internal void CopyTo(SacredActorInventory target) => target.Update(() =>
    {
        foreach (var item in _items) target._items.Add(item.Key, item.Value);
        Backpack.CopyTo(target.Backpack);
        for (var index = 0; index < EquipmentSlots.Count; index++)
            if (EquipmentSlots[index].Instance is { } item) target.EquipmentSlots[index].Equip(item);
    });
}
