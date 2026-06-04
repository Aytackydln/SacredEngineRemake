using System.Collections.Frozen;
using System.Collections.Generic;
using Sacred.Core.Weapon;

namespace SacredItemSimulator.Avalonia.ItemViewer;

public readonly record struct SacredItemDataModel(
    uint ItemId,
    string ItemName,
    string ModelName,
    byte Width,
    byte Height,
    string TextureName,
    byte[] UnknownBytes
)
{
    public static SacredItemDataModel FromSacredEquipment(SacredEquipment equipment, FrozenDictionary<string, string> translationMap)
    {
        return new SacredItemDataModel(
            ItemId: equipment.IdemId,
            ItemName: translationMap.GetValueOrDefault(equipment.Name, equipment.Name),
            ModelName: equipment.Item.ModelDesc.ModelName,
            Width: equipment.Width,
            Height: equipment.Height,
            TextureName: "", // TODO figure out how to get texture name
            UnknownBytes: equipment.UnknownBytes
        );
    }
}