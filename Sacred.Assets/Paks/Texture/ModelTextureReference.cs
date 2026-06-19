namespace Sacred.Assets.Paks.Texture;

public enum TextureOverlayMode
{
    None = 0,
    AlphaBlend = 1,
    MultiTextureFill = 2
}

public readonly record struct ModelTextureReference(
    string TextureName,
    TextureAnimation Animation,
    string? OverlayTextureName = null,
    TextureAnimation OverlayAnimation = default,
    TextureOverlayMode OverlayMode = TextureOverlayMode.None)
{
    public static ModelTextureReference Static(string textureName) =>
        new(textureName, TextureAnimation.None);

    public bool HasOverlay =>
        !string.IsNullOrWhiteSpace(OverlayTextureName) &&
        OverlayMode != TextureOverlayMode.None;
}
