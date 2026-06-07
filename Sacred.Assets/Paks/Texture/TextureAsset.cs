namespace Sacred.Assets.Paks.Texture;

public sealed record TextureAsset(string Name, int Width, int Height, byte[] Rgba8);

public sealed record StaticSpriteAsset(uint GroupId, int Width, int Height, int AnchorX, int AnchorY, byte[] Rgba);
