using Sacred.Granny;

namespace Sacred.Engine.Assets;

public sealed record PlayerCharacterAsset(
    uint ItemId,
    string DisplayName,
    string ModelName,
    GrnAsset Model);
