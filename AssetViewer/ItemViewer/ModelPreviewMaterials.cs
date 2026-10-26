using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Granny.Meshes;
using Sacred.Shaders;

namespace AssetViewer.ItemViewer;

internal enum PreviewMaterialPass { Static, AnimatedBase, EffectOverlay }
internal readonly record struct PreviewMaterialTexture(TextureAnimation Animation, TextureAnimation OverlayAnimation,
    TextureOverlayMode OverlayMode, bool HasOverlay, bool Translucent);
internal readonly record struct PreviewMaterialDraw(MeshSurface Surface, PreviewMaterialPass Pass, Vector4 Color,
    float TextureMode, float AnimationValue, float TimeScale, bool UsesOverlay, bool Translucent);

/// <summary>Native model material ordering and texture animation flags for every renderer.</summary>
internal static class ModelPreviewMaterials
{
    internal static bool HasTranslucentPixels(ReadOnlySpan<byte> rgba)
    {
        for (var index = 3; index < rgba.Length; index += 4)
            if (rgba[index] is not 0 and not byte.MaxValue) return true;
        return false;
    }

    internal static IEnumerable<PreviewMaterialDraw> Enumerate(IEnumerable<MeshSurface> surfaces, Mesh? mesh,
        string name, Func<string?, PreviewMaterialTexture?> resolve, int secondaryGlowSlot)
    {
        var defaultColor = ModelShaderVariables.ColorFromName(name);
        for (var index = 0; index < 3; index++)
        {
            var pass = (PreviewMaterialPass)index;
            foreach (var surface in surfaces)
            {
                var texture = resolve(surface.TextureName);
                var animated = texture?.Animation.IsAnimated == true;
                var overlay = texture is { HasOverlay: true, OverlayMode: not TextureOverlayMode.None };
                var animatedOverlay = overlay && texture!.Value.OverlayAnimation.IsAnimated;
                var animation = TextureAnimation.None;
                var drawOverlay = false;
                if (pass == PreviewMaterialPass.AnimatedBase)
                {
                    if (!animated || texture is null) continue;
                    overlay = false; animation = texture.Value.Animation;
                }
                else if (pass == PreviewMaterialPass.EffectOverlay)
                {
                    if (animated || !animatedOverlay || texture is null) continue;
                    drawOverlay = true; animation = texture.Value.OverlayAnimation;
                }
                else
                {
                    if (animated || animatedOverlay) continue;
                    overlay &= !animatedOverlay;
                }
                var color = animation.Mode == TextureAnimationMode.RadialSweepBlackKey && mesh is not null &&
                    MeshSurfaceRadialSweep.TryCalculate(mesh, surface, out var radial) ? radial : defaultColor;
                if (animation.HasSecondaryGlow) color.W = secondaryGlowSlot;
                yield return new(surface, pass, color,
                    ModelShaderVariables.PackTextureMode(texture is not null, drawOverlay || overlay,
                        drawOverlay || texture?.OverlayMode == TextureOverlayMode.MultiTextureFill),
                    animation.Mode == TextureAnimationMode.NativeItemTransform
                        ? ModelShaderVariables.PackNativeItemTextureAnimation(animation.NativeEffectSelector)
                        : ModelShaderVariables.PackTextureAnimation(animation.IsAnimated,
                            animation.Mode == TextureAnimationMode.RadialSweepBlackKey, false),
                    animation.IsAnimated ? animation.TimeScale : 0,
                    drawOverlay || overlay, texture?.Translucent == true);
            }
        }
    }
}
