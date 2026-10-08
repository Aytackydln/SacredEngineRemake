using System;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Inventory.Actors;

/// <summary>Native equipment placement in the actor skeleton. Occurrences count all slots of
/// the same type, including empty slots, so an empty first ring never swaps the second's hand.</summary>
public static class EquipmentModelPlacement
{
    public static (string? TargetBone, string? SourceBone) Resolve(
        EquipmentSlotType slot, SacredEquipmentType? equipmentType, int occurrence = 0)
    {
        if (occurrence < 0 || slot == EquipmentSlotType.Ring && occurrence >= 4)
            throw new ArgumentOutOfRangeException(nameof(occurrence));
        // Demo 0x401A10 / Gold 0x401AE0: native ring slots 8/10 use L Finger31,
        // 9/11 use R Finger31. connectGranny uses GRNAB_WEAPON1 as the ring pivot.
        // Amulet slots are EQUIPSLOT_WEAR; their authored skeleton supplies placement.
        return (slot, equipmentType) switch
        {
            (EquipmentSlotType.Ring, _) =>
                (occurrence % 2 == 0 ? "Bip01 L Finger31" : "Bip01 R Finger31", "Bone_weapon_01"),
            (EquipmentSlotType.LeftHand, SacredEquipmentType.Shield) => ("Bip01 L Forearm", "Bone_weapon_02"),
            (EquipmentSlotType.LeftHand, _) => ("Bip01 L Hand", "Bone_weapon_02"),
            (EquipmentSlotType.RightHand, _) => ("Bip01 R Hand", "Bone_weapon_01"),
            (EquipmentSlotType.Wings, SacredEquipmentType.Wings) => ("Bip01 Spine2", "Bone_spine"),
            _ => (null, null)
        };
    }
}
