using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.Pak.Weapon;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

public readonly record struct SacredItemDataModel(
    uint ItemId,
    string ItemName,
    SacredCharacterClassMask CharacterClassMask,
    SacredEquipmentType EquipmentType,
    Vector3 PreviewRotation,
    string ModelName,
    uint TextureId,
    uint EffectTextureId,
    uint GraphicRenderFlags,
    ushort EffectAnimationRate,
    SacredEquipmentDamage Damage,
    byte Width,
    byte Height,
    SacredEquipmentRarityTier Rarity,
    bool IsFavorite = false,
    bool PreviewConfirmed = false,
    DateTimeOffset? PreviewConfirmedAt = null,
    bool PreviewConfirmedUserRotationIsZero = false
)
{
    public string FavoriteDisplay => IsFavorite ? "★" : "☆";

    public string PreviewConfirmedDisplay => PreviewConfirmed
        ? (PreviewConfirmedUserRotationIsZero ? "✓" : "X")
        : "";

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
            EffectTextureId: equipment.Item.EffectTextureId,
            GraphicRenderFlags: equipment.Item.GraphicRenderFlags,
            EffectAnimationRate: equipment.Item.ModelDesc.EffectAnimationRate,
            Damage: equipment.Damage,
            EquipmentType: equipment.EquipmentType,
            Rarity: equipment.RarityTier,
            PreviewRotation: equipment.PreviewRotation,
            Width: equipment.Width,
            Height: equipment.Height
        );
    }
}
