using Sacred.Assets.Paks.Texture;

namespace Sacred.World.Map;

public sealed record WorldMapAtlas(
    int Width,
    int Height,
    byte[] Rgba,
    TextureAsset PlayerMarker);
