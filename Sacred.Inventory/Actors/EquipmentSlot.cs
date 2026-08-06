using System;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Inventory.Actors;

public sealed class EquipmentSlot(EquipmentSlotType type)
{
    public EquipmentSlotType Type { get; } = type;
    public SacredEquipment? Equipment { get; private set; }

    public void Equip(SacredEquipment equipment) => Equipment = equipment;
    public void Unequip() => Equipment = null;
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
