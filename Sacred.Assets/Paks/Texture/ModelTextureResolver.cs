namespace Sacred.Assets.Paks.Texture;

public static class ModelTextureResolver
{
    private const int AnimatedFrameCount = 4;
    private const float AnimatedFramesPerSecond = 4.0f;
    // Equipment multitexture rows use this flag for scrolling fill effects without a frame-strip texture.
    private const uint MultitextureScrollEffectFlag = 0x0010_0000;
    private const uint VerticalScrollEffectFlag = 0x0020_0000;
    private const uint PrimaryTextureTableLimit = byte.MaxValue;

    public static ModelTextureReference Resolve(
        TexturePakArchive textureArchive,
        uint itemTextureId,
        uint effectTextureId,
        uint graphicRenderFlags,
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
            if (hasEffectTexture &&
                string.Equals(resolvedSurfaceName, effectTextureName, StringComparison.OrdinalIgnoreCase))
            {
                return new ModelTextureReference(
                    resolvedSurfaceName,
                    InferEffectAnimation(textureArchive, itemTextureName, resolvedSurfaceName, graphicRenderFlags));
            }

            if (hasEffectTexture)
                return new ModelTextureReference(
                    resolvedSurfaceName,
                    TextureAnimation.None,
                    effectTextureName,
                    InferEffectAnimation(textureArchive, resolvedSurfaceName, effectTextureName, graphicRenderFlags),
                    InferOverlayMode(effectTextureId));

            return new ModelTextureReference(
                resolvedSurfaceName,
                TextureAnimation.None);
        }

        if (hasItemTexture)
        {
            if (hasEffectTexture)
                return new ModelTextureReference(
                    itemTextureName,
                    TextureAnimation.None,
                    effectTextureName,
                    InferEffectAnimation(textureArchive, itemTextureName, effectTextureName, graphicRenderFlags),
                    InferOverlayMode(effectTextureId));

            return new ModelTextureReference(
                itemTextureName,
                TextureAnimation.None);
        }

        if (hasEffectTexture)
            return new ModelTextureReference(
                effectTextureName,
                InferEffectAnimation(textureArchive, null, effectTextureName, graphicRenderFlags));

        return string.IsNullOrWhiteSpace(surfaceTextureName)
            ? new ModelTextureReference(string.Empty, TextureAnimation.None)
            : ModelTextureReference.Static(surfaceTextureName);
    }

    private static TextureOverlayMode InferOverlayMode(uint effectTextureId) =>
        effectTextureId <= PrimaryTextureTableLimit
            ? TextureOverlayMode.MultiTextureFill
            : TextureOverlayMode.AlphaBlend;

    private static TextureAnimation InferEffectAnimation(
        TexturePakArchive textureArchive,
        string? baseTextureName,
        string effectTextureName,
        uint graphicRenderFlags)
    {
        if (!textureArchive.TryResolveTextureRecord(effectTextureName, out var effectRecord))
            return TextureAnimation.None;

        if (!string.IsNullOrWhiteSpace(baseTextureName) &&
            textureArchive.TryResolveTextureRecord(baseTextureName, out var baseRecord) &&
            effectRecord.Height > baseRecord.Height)
        {
            return new TextureAnimation(
                AnimatedFrameCount,
                AnimatedFramesPerSecond,
                TextureAnimationMode.FrameStrip);
        }

        if ((graphicRenderFlags & (MultitextureScrollEffectFlag | VerticalScrollEffectFlag)) != 0)
            return new TextureAnimation(
                1,
                AnimatedFramesPerSecond,
                TextureAnimationMode.VerticalScrollBlackKey);

        return TextureAnimation.None;
    }
}
