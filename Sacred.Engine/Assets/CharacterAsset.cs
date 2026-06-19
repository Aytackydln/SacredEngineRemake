using System.Collections.Generic;
using Sacred.Assets.Paks.Texture;
using Sacred.Granny;

namespace Sacred.Engine.Assets;

public sealed record PlayerCharacterAsset(
    uint ItemId,
    string DisplayName,
    string ModelName,
    GrnAsset Model,
    IReadOnlyDictionary<string, ModelTextureReference> TextureAliases);
