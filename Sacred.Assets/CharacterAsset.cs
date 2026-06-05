using Sacred.Granny;

namespace Sacred.Assets;

public sealed record PlayerCharacterAsset(uint SlotId, string DisplayName, string ModelName, GrnAsset Model);
