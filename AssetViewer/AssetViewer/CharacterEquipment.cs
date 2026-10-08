using System;
using Sacred.Assets.Paks.Models;
using Sacred.Core.Pak.Weapon;
using Sacred.Inventory.Actors;

namespace AssetViewer.AssetViewer;

public sealed class CharacterSlotRow(int index, EquipmentSlot slot)
{
    internal EquipmentSlot Slot { get; } = slot;
    public int Index { get; } = index;
    public EquipmentSlotType Type => Slot.Type;
    internal int Occurrence { get; init; }
    public string Equipment => Slot.Instance is { } item ? $"{item.Definition.IdemId}: {item.Definition.Name} · Lv {item.Level}" : "(empty)";
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
        return EquipmentSlotRules.AcceptsItem(slot, item);
    }

    public static ModelAttachmentReference Attachment(CharacterSlotRow slot)
    {
        var item = slot.Slot.Equipment ?? throw new InvalidOperationException("Slot is empty.");
        var bones = EquipmentModelPlacement.Resolve(slot.Type, item.EquipmentType, slot.Occurrence);
        return new ModelAttachmentReference(item.Item.ModelName, bones.Item1, bones.Item2);
    }
}
