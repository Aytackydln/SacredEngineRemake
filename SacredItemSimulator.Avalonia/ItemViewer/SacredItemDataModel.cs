using System.Collections.Frozen;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.Weapon;

namespace SacredItemSimulator.Avalonia.ItemViewer;

public readonly record struct SacredItemDataModel(
    uint ItemId,
    string ItemName,
    SacredCharacterClassMask CharacterClassMask,
    SacredEquipmentType EquipmentType,
    string ModelName,
    Vector3 PreviewRotation,
    byte Width,
    byte Height,
    byte[] UnknownBytes
)
{
    public static SacredItemDataModel FromSacredEquipment(SacredEquipment equipment, FrozenDictionary<string, string> translationMap)
    {
        return new SacredItemDataModel(
            ItemId: equipment.IdemId,
            ItemName: translationMap.GetValueOrDefault(equipment.Name, equipment.Name),
            CharacterClassMask: equipment.EffectiveCharacterClassMask,
            ModelName: equipment.Item.ModelDesc.ModelName,
            EquipmentType: equipment.EquipmentType,
            PreviewRotation: equipment.PreviewRotation,
            Width: equipment.Width,
            Height: equipment.Height,
            UnknownBytes: equipment.UnknownBytes
        );
    }
}
