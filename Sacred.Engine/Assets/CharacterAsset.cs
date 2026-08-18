using System.Collections.Generic;
using Sacred.Assets.Paks.Models;
using Sacred.Assets.Paks.Texture;
using Sacred.Granny;
using Sacred.Granny.Assets;
using Sacred.Inventory.Effects;

namespace Sacred.Engine.Assets;

public sealed record PlayerCharacterAsset(
    uint ItemId,
    string DisplayName,
    string ModelName,
    GrnAsset Model,
    IReadOnlyDictionary<string, ModelTextureReference> TextureAliases,
    EquipmentEffectScene? EquipmentEffects,
    CharacterMotionWeaponStyle WeaponStyle);
