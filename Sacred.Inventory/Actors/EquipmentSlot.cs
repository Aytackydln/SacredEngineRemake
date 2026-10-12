using System;
using Sacred.Core.Pak.Weapon;
using Sacred.Inventory.Items;

namespace Sacred.Inventory.Actors;

public sealed class EquipmentSlot(EquipmentSlotType type)
{
    public EquipmentSlotType Type { get; } = type;
    public SacredItemInstance? Instance { get; private set; }
    public SacredEquipment? Equipment => Instance?.Definition;
    /// <summary>Authored appearance for an empty slot; never an owned or equipped item.</summary>
    public SacredEquipment? DefaultAppearance { get; private set; }
    public SacredEquipment? VisualEquipment => Equipment ?? DefaultAppearance;
    public event Action? Changed;

    internal void SetDefaultAppearance(SacredEquipment? equipment)
    {
        DefaultAppearance = equipment;
        Changed?.Invoke();
    }

    public void Equip(SacredEquipment equipment) => Equip(SacredItemInstance.FromDefinition(equipment));
    public void Equip(SacredItemInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (instance.Definition.IsAppearanceFallback)
            throw new ArgumentException("Appearance fallbacks cannot be inventory items.", nameof(instance));
        Instance = instance;
        Changed?.Invoke();
    }
    public void Unequip()
    {
        Instance = null;
        Changed?.Invoke();
    }
}

public enum EquipmentSlotType
{
    LeftHand,
    RightHand,
    Head,
    Shoulder,
    Arms,
    Hands,
    Body,
    Wings,
    Cannon,
    Amulet,
    Ring,
    Feet,
    Legs,
    Belt,
    SmallBelt
}

/// <summary>Identifies an individual slot, including repeated rings and amulets.</summary>
public enum ItemSlot
{
    LeftHand, RightHand, Head, Shoulder, Arms, Hands, Body, Wings, Cannon,
    Amulet1, Amulet2, Ring1, Ring2, Ring3, Ring4, Feet, Legs, Belt
}

public static class ItemSlotExtensions
{
    public static EquipmentSlotType ToEquipmentSlotType(this ItemSlot slot) => slot switch
    {
        ItemSlot.LeftHand => EquipmentSlotType.LeftHand,
        ItemSlot.RightHand => EquipmentSlotType.RightHand,
        ItemSlot.Head => EquipmentSlotType.Head,
        ItemSlot.Shoulder => EquipmentSlotType.Shoulder,
        ItemSlot.Arms => EquipmentSlotType.Arms,
        ItemSlot.Hands => EquipmentSlotType.Hands,
        ItemSlot.Body => EquipmentSlotType.Body,
        ItemSlot.Wings => EquipmentSlotType.Wings,
        ItemSlot.Cannon => EquipmentSlotType.Cannon,
        ItemSlot.Amulet1 or ItemSlot.Amulet2 => EquipmentSlotType.Amulet,
        ItemSlot.Ring1 or ItemSlot.Ring2 or ItemSlot.Ring3 or ItemSlot.Ring4 => EquipmentSlotType.Ring,
        ItemSlot.Feet => EquipmentSlotType.Feet,
        ItemSlot.Legs => EquipmentSlotType.Legs,
        ItemSlot.Belt => EquipmentSlotType.Belt,
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };
}
