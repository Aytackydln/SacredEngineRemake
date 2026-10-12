using Sacred.Core.Pak.Items;

namespace Sacred.Assets.Paks.Texture;

public static class ModelTextureResolver
{

    public static ModelTextureReference Resolve(ITextureSource textureArchive,
        uint itemTextureId,
        uint effectTextureId,
        SacredItemGraphicFlags graphicFlags,
        bool modelHasEffectTextureSurface,
        bool preferItemTexture,
        string? surfaceTextureName,
        byte effectSelector = 0)
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
                        graphicFlags,
                        effectSelector));
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
                        graphicFlags,
                        effectSelector),
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
                        graphicFlags,
                        effectSelector),
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
                    graphicFlags,
                    effectSelector));

        return string.IsNullOrWhiteSpace(surfaceTextureName)
            ? new ModelTextureReference(string.Empty, TextureAnimation.None)
            : ModelTextureReference.Static(surfaceTextureName);
    }

    private static TextureAnimation CreateEffectAnimation(ITextureSource textureArchive,
        string effectTextureName,
        SacredItemGraphicFlags graphicFlags,
        byte effectSelector)
    {
        // Gold getComplexFX_ID (0x426520) gates the authored selector on bit 20.
        if ((graphicFlags & SacredItemGraphicFlags.MultitextureScroll) == 0 ||
            !textureArchive.TryResolveTextureRecord(effectTextureName, out _))
            return TextureAnimation.None;

        return effectSelector is 1 or 2 or 3 or 4 or 6 or 9
            ? new TextureAnimation(TextureAnimationMode.NativeItemTransform, 1, effectSelector)
            : TextureAnimation.None;
    }
}
