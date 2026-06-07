using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.Weapon;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

public readonly record struct SacredItemDataModel(
    uint ItemId,
    string ItemName,
    SacredCharacterClassMask CharacterClassMask,
    SacredEquipmentType EquipmentType,
    string ModelName,
    uint TextureId,
    Vector3 PreviewRotation,
    byte Width,
    byte Height,
    bool PreviewConfirmed = false,
    DateTimeOffset? PreviewConfirmedAt = null
)
{
    public string PreviewConfirmationStatus => PreviewConfirmed
        ? $"Confirmed {PreviewConfirmedAt:yyyy-MM-dd HH:mm}"
        : "Unconfirmed";

    public static SacredItemDataModel FromSacredEquipment(SacredEquipment equipment, FrozenDictionary<string, string> translationMap)
    {
        return new SacredItemDataModel(
            ItemId: equipment.IdemId,
            ItemName: translationMap.GetValueOrDefault(equipment.Name, equipment.Name),
            CharacterClassMask: equipment.EffectiveCharacterClassMask,
            ModelName: equipment.Item.ModelDesc.ModelName,
            TextureId: equipment.Item.ModelDesc.TextureId,
            EquipmentType: equipment.EquipmentType,
            PreviewRotation: equipment.PreviewRotation,
            Width: equipment.Width,
            Height: equipment.Height
        );
    }
}
