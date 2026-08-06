namespace Sacred.Assets.Paks.Texture;

public static class ModelTextureResolver
{
    // Sacred's item descriptor value at offset 112 is unrelated to effect timing.
    // Scrolling equipment materials use a common cadence in the game.
    private const float EffectScrollCyclesPerSecond = 0.5f;
    // Equipment multitexture rows use this flag for scrolling fill effects without a frame-strip texture.
    private const uint MultitextureScrollEffectFlag = 0x0010_0000;
    private const uint VerticalScrollEffectFlag = 0x0020_0000;
    private const uint PrimaryTextureTableLimit = byte.MaxValue;

    public static ModelTextureReference Resolve(
        TexturePakArchive textureArchive,
        uint itemTextureId,
        uint effectTextureId,
        uint graphicRenderFlags,
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
                        graphicRenderFlags,
                        clampAtTextureEdges: false));
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
                        graphicRenderFlags,
                        ShouldClampOverlay(effectTextureId, graphicRenderFlags)),
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
                        graphicRenderFlags,
                        ShouldClampOverlay(effectTextureId, graphicRenderFlags)),
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
                    graphicRenderFlags,
                    clampAtTextureEdges: false));

        return string.IsNullOrWhiteSpace(surfaceTextureName)
            ? new ModelTextureReference(string.Empty, TextureAnimation.None)
            : ModelTextureReference.Static(surfaceTextureName);
    }

    private static TextureAnimation CreateEffectAnimation(
        TexturePakArchive textureArchive,
        string effectTextureName,
        uint graphicRenderFlags,
        bool clampAtTextureEdges)
    {
        if ((graphicRenderFlags & (MultitextureScrollEffectFlag | VerticalScrollEffectFlag)) == 0)
            return TextureAnimation.None;

        if (!textureArchive.TryResolveTextureRecord(effectTextureName, out _))
            return TextureAnimation.None;

        return new TextureAnimation(
            clampAtTextureEdges
                ? TextureAnimationMode.RadialSweepBlackKey
                : TextureAnimationMode.VerticalScrollBlackKey,
            EffectScrollCyclesPerSecond);
    }

    private static bool ShouldClampOverlay(uint effectTextureId, uint graphicRenderFlags) =>
        // External vertical-effect textures are one-shot bands that cross the UV
        // map before repeating. Primary-table multitextures are tiled fills.
        effectTextureId > PrimaryTextureTableLimit &&
        (graphicRenderFlags & VerticalScrollEffectFlag) != 0;
}
