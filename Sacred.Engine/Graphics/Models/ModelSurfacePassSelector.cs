using Sacred.Assets.Paks.Texture;
using Sacred.Shaders;

namespace Sacred.Engine.Graphics.Models;

internal static class ModelSurfacePassSelector
{
    public static bool TrySelect(
        ModelSurfacePass pass,
        ModelTextureReference textureReference,
        bool animatesBase,
        bool animatesOverlay,
        bool hasTexture,
        bool hasOverlayResource,
        out float textureMode,
        out TextureAnimation animation,
        out bool hasOverlay)
    {
        hasOverlay = false;
        textureMode = ModelShaderVariables.TextureModeNoTexture;
        animation = TextureAnimation.None;

        if (pass == ModelSurfacePass.AnimatedBase)
        {
            if (!animatesBase || !hasTexture)
                return false;

            textureMode = ModelShaderVariables.PackTextureMode(hasTexture, false, false);
            animation = textureReference.Animation;
            return true;
        }

        if (pass == ModelSurfacePass.EffectOverlay)
        {
            if (animatesBase || !animatesOverlay || !hasTexture || !hasOverlayResource)
                return false;

            hasOverlay = true;
            textureMode = ModelShaderVariables.PackTextureMode(hasTexture, true, true);
            animation = textureReference.OverlayAnimation;
            return true;
        }

        if (animatesBase || animatesOverlay && hasOverlayResource)
            return false;

        hasOverlay = hasOverlayResource && !animatesOverlay;
        textureMode = ModelShaderVariables.PackTextureMode(
            hasTexture,
            hasOverlay,
            textureReference.OverlayMode == TextureOverlayMode.MultiTextureFill);
        return true;
    }
}

internal enum ModelSurfacePass
{
    Static = 0,
    AnimatedBase = 1,
    EffectOverlay = 2
}
