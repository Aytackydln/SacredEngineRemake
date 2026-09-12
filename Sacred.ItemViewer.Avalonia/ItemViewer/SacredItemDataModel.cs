using System;
using System.Globalization;
using System.Numerics;
using Sacred.Core.GameRes;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

public readonly record struct SacredItemDataModel(
    uint ItemId,
    uint BaseItemId,
    string ItemName,
    SacredCharacterClassMask CharacterClassMask,
    SacredEquipmentType EquipmentType,
    Vector3 PreviewRotation,
    string ModelName,
    uint TextureId,
    uint EffectTextureId,
    byte ItemEffectSelector,
    SacredItemGraphicFlags GraphicFlags,
    SacredItemCategory Category,
    SacredEquipmentDamage Damage,
    SacredEquipmentBonusTypes BonusTypes,
    SacredEquipmentBonusGroups BonusGroups,
    byte Width,
    byte Height,
    SacredEquipmentRarityTier Rarity,
    bool IsFavorite = false,
    bool PreviewConfirmed = false,
    DateTimeOffset? PreviewConfirmedAt = null,
    bool PreviewConfirmedUserRotationIsZero = false
)
{
    // Parsed equipment contains inline arrays, which cannot use the record's generated ValueType.Equals.
    // Weapon.pak item IDs are the stable identity for rows and their favorite/confirmation variants.
    public bool Equals(SacredItemDataModel other)
    {
        return ItemId == other.ItemId;
    }

    public override int GetHashCode()
    {
        return ItemId.GetHashCode();
    }

    public string FavoriteDisplay => IsFavorite ? "★" : "☆";

    public string PreviewConfirmedDisplay => PreviewConfirmed
        ? PreviewConfirmedUserRotationIsZero ? "✓" : "X"
        : "";

    public string PreviewConfirmationStatus => PreviewConfirmed
        ? $"Confirmed {PreviewConfirmedAt:yyyy-MM-dd HH:mm}"
        : "Unconfirmed";

    public static SacredItemDataModel FromSacredEquipment(SacredEquipment equipment, GameResStore resources)
    {
        return new SacredItemDataModel(
            ItemId: equipment.IdemId,
            BaseItemId: equipment.BaseItemId,
            ItemName: resources.GetString(
                equipment.IdemId.ToString(CultureInfo.InvariantCulture),
                equipment.Name),
            CharacterClassMask: equipment.EffectiveCharacterClassMask,
            ModelName: equipment.Item.ModelName,
            TextureId: equipment.Item.ModelDesc.TextureId,
            EffectTextureId: equipment.Item.ModelDesc.EffectTextureId,
            ItemEffectSelector: equipment.Item.ModelDesc.EffectTextureIndex,
            GraphicFlags: equipment.Item.ModelDesc.GraphicFlags,
            Category: equipment.Item.ModelDesc.Category,
            Damage: equipment.Damage,
            BonusTypes: equipment.BonusTypes,
            BonusGroups: equipment.BonusGroups,
            EquipmentType: equipment.EquipmentType,
            Rarity: equipment.RarityTier,
            PreviewRotation: equipment.PreviewRotation,
            Width: equipment.Width,
            Height: equipment.Height
        );
    }
}
