using Sacred.Assets.Paks.Texture;

namespace AssetViewer.ItemViewer;

public sealed record ModelTextureBinding(
    TextureAsset BaseTexture,
    TextureAsset? OverlayTexture = null,
    TextureOverlayMode OverlayMode = TextureOverlayMode.None)
{
    public bool HasOverlay =>
        OverlayTexture is not null &&
        OverlayMode != TextureOverlayMode.None;
}
