using System;
using System.Linq;
using Sacred.Assets.Paks.Models;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Inventory.Actors;

namespace AssetViewer.AssetViewer;

public sealed class CharacterSlotRow(int index, EquipmentSlot slot)
{
    internal EquipmentSlot Slot { get; } = slot;
    public int Index { get; } = index;
    public EquipmentSlotType Type => Slot.Type;
    public string Equipment => Slot.Equipment is { } item ? $"{item.IdemId}: {item.Name}" : "(empty)";
    public override string ToString() => $"{Index}: {Type} · {Equipment}";
}

public sealed class CharacterEquipmentRow(SacredEquipment equipment, string name)
{
    internal SacredEquipment Item { get; } = equipment;
    public uint EntryId => Item.IdemId;
    public string Name { get; } = name;
    public SacredEquipmentType Type => Item.EquipmentType;
    public string Model => Item.Item.ModelName;
    public override string ToString() => $"{EntryId}: {Name} ({Type})";
}

internal static class CharacterEquipment
{
    public static bool CanEquip(EquipmentSlotType slot, SacredCharacterClass? characterClass, SacredEquipment item)
    {
        if (characterClass is { } restriction && !EquipmentSlotRules.CanEquip(restriction, item)) return false;
        if (item.Item.ModelDesc.Category == SacredItemCategory.DwarfCannon) return slot == EquipmentSlotType.Cannon;
        if (item.EquipmentType is SacredEquipmentType.Misc or SacredEquipmentType.Briddle) return false;
        var requested = EquipmentSlotRules.GetSlotType(item.EquipmentType);
        if (requested == EquipmentSlotType.RightHand) return slot is EquipmentSlotType.LeftHand or EquipmentSlotType.RightHand;
        return EquipmentSlotRules.Accepts(slot, requested);
    }

    public static void Equip(CharacterSlotRow[] slots, CharacterSlotRow target, SacredEquipment equipment)
    {
        if (target.Type is EquipmentSlotType.LeftHand or EquipmentSlotType.RightHand)
            foreach (var other in slots.Where(row => row != target && (row.Type is EquipmentSlotType.LeftHand or EquipmentSlotType.RightHand)))
                if (equipment.InferredTwoHanded == true || other.Slot.Equipment?.InferredTwoHanded == true) other.Slot.Unequip();
        target.Slot.Equip(equipment);
    }

    public static ModelAttachmentReference Attachment(CharacterSlotRow slot)
    {
        var item = slot.Slot.Equipment ?? throw new InvalidOperationException("Slot is empty.");
        var bones = (slot.Type, item.EquipmentType) switch
        {
            (EquipmentSlotType.LeftHand, SacredEquipmentType.Shield) => ("Bip01 L Forearm", "Bone_weapon_02"),
            (EquipmentSlotType.LeftHand, _) => ("Bip01 L Hand", "Bone_weapon_02"),
            (EquipmentSlotType.RightHand, _) => ("Bip01 R Hand", "Bone_weapon_01"),
            (EquipmentSlotType.Wings, SacredEquipmentType.Wings) => ("Bip01 Spine2", "Bone_spine"),
            _ => ((string?)null, (string?)null)
        };
        return new ModelAttachmentReference(item.Item.ModelName, bones.Item1, bones.Item2);
    }
}
