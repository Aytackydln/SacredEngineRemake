namespace Sacred.Assets.Paks.Texture;

public static class ModelTextureResolver
{
    private const float ScrollSpeedScale = 1000.0f;
    // Equipment multitexture rows use this flag for scrolling fill effects without a frame-strip texture.
    private const uint MultitextureScrollEffectFlag = 0x0010_0000;
    private const uint VerticalScrollEffectFlag = 0x0020_0000;
    private const uint PrimaryTextureTableLimit = byte.MaxValue;

    public static ModelTextureReference Resolve(
        TexturePakArchive textureArchive,
        uint itemTextureId,
        uint effectTextureId,
        uint graphicRenderFlags,
        ushort effectAnimationRate,
        bool modelHasEffectTextureSurface,
        bool preferItemTexture,
        string? surfaceTextureName)
    {
        var itemTextureName = string.Empty;
        var hasItemTexture = itemTextureId > 0 &&
                             textureArchive.TryGetTextureName(itemTextureId, out itemTextureName);
        var effectTextureName = string.Empty;
        var hasEffectTexture = effectTextureId > 0 &&
                               textureArchive.TryGetTextureName(effectTextureId, out effectTextureName);

        if (!string.IsNullOrWhiteSpace(surfaceTextureName) &&
            textureArchive.TryResolveTextureName(surfaceTextureName, out var resolvedSurfaceName))
        {
            if (hasEffectTexture && string.Equals(resolvedSurfaceName, effectTextureName, StringComparison.OrdinalIgnoreCase))
            {
                return new ModelTextureReference(
                    resolvedSurfaceName,
                    CreateEffectAnimation(
                        textureArchive,
                        resolvedSurfaceName,
                        effectAnimationRate));
            }

            // Single-material GRNs commonly embed an export-time default; Items.pak selects the item variant.
            var baseTextureName = preferItemTexture && hasItemTexture
                ? itemTextureName
                : resolvedSurfaceName;
            if (hasEffectTexture && !modelHasEffectTextureSurface)
            {
                return new ModelTextureReference(
                    baseTextureName,
                    TextureAnimation.None,
                    effectTextureName,
                    CreateEffectAnimation(
                        textureArchive,
                        effectTextureName,
                        effectAnimationRate),
                    TextureOverlayMode.MultiTextureFill);
            }

            return new ModelTextureReference(
                baseTextureName,
                TextureAnimation.None);
        }

        if (hasItemTexture)
        {
            if (hasEffectTexture && !modelHasEffectTextureSurface)
            {
                return new ModelTextureReference(
                    itemTextureName,
                    TextureAnimation.None,
                    effectTextureName,
                    CreateEffectAnimation(
                        textureArchive,
                        effectTextureName,
                        effectAnimationRate),
                    TextureOverlayMode.MultiTextureFill);
            }

            return new ModelTextureReference(
                itemTextureName,
                TextureAnimation.None);
        }

        if (hasEffectTexture)
            return new ModelTextureReference(
                effectTextureName,
                CreateEffectAnimation(
                    textureArchive,
                    effectTextureName,
                    effectAnimationRate));

        return string.IsNullOrWhiteSpace(surfaceTextureName)
            ? new ModelTextureReference(string.Empty, TextureAnimation.None)
            : ModelTextureReference.Static(surfaceTextureName);
    }

    private static TextureAnimation CreateEffectAnimation(
        TexturePakArchive textureArchive,
        string effectTextureName,
        ushort effectAnimationRate)
    {
        if (effectAnimationRate == 0)
            return TextureAnimation.None;

        if (!textureArchive.TryResolveTextureRecord(effectTextureName, out _))
            return TextureAnimation.None;

        return new TextureAnimation(
            TextureAnimationMode.VerticalScrollBlackKey,
            effectAnimationRate / ScrollSpeedScale);
    }
}
