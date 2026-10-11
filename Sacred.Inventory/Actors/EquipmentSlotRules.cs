using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Inventory.Actors;

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
