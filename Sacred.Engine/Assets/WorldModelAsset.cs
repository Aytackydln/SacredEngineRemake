using System.Collections.Generic;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;
using Sacred.Granny.Animation;
using Sacred.Granny.Assets;
using Sacred.Inventory.Effects;

namespace Sacred.Engine.Assets;

/// <summary>A model-backed static world item with its Items.pak material bindings.</summary>
public sealed record WorldModelAsset(
    ItemsPakEntry Item,
    GrnAsset Model,
    IReadOnlyDictionary<string, ModelTextureReference> TextureAliases,
    GrnAnimationClip? OpenAnimation,
    GrnAnimationClip? CloseAnimation)
{
    public EquipmentEffectScene? Effects { get; init; }
}
