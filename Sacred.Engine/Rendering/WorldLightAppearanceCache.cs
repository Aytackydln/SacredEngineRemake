using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;

namespace Sacred.Engine.Rendering;

/// <summary>
/// Derives the position and colour of an engine-rendered light halo from its
/// animated emitter sprite. The halo diameter remains sourced from items.pak.
/// </summary>
internal sealed class WorldLightAppearanceCache
{
    private const uint UnlitGraphicFlag = 0x00020000;
    private const uint LowRenderFlagMask = 0x0000000F;
    private const uint AnimatedMiniObjectRenderFlags = 0x00000008;
    private const float HaloOpacity = 0.34f;

    private readonly Dictionary<StaticSpriteAsset, WorldLightAppearance?> _appearances =
        new(ReferenceEqualityComparer.Instance);

    public bool TryGet(
        ItemsPakEntry item,
        StaticSpriteAsset sprite,
        out WorldLightAppearance appearance)
    {
        if (!IsLightEmitter(item, sprite))
        {
            appearance = default;
            return false;
        }

        if (!_appearances.TryGetValue(sprite, out var cached))
        {
            cached = DeriveAppearance(sprite);
            _appearances.Add(sprite, cached);
        }

        if (cached is not { } value)
        {
            appearance = default;
            return false;
        }

        appearance = value;
        return true;
    }

    private static bool IsLightEmitter(ItemsPakEntry item, StaticSpriteAsset sprite) =>
        item.ModelDesc.ModelExtent > 0 &&
        sprite.FrameCount > 1 &&
        (item.GraphicRenderFlags & UnlitGraphicFlag) != 0 &&
        (item.GraphicRenderFlags & LowRenderFlagMask) == AnimatedMiniObjectRenderFlags;

    private static WorldLightAppearance? DeriveAppearance(StaticSpriteAsset sprite)
    {
        var rgba = sprite.Rgba;
        if (rgba.Length != checked(sprite.AtlasWidth * sprite.AtlasHeight * 4))
            return null;

        double weightSum = 0;
        double xSum = 0;
        double ySum = 0;
        double redSum = 0;
        double greenSum = 0;
        double blueSum = 0;

        for (var atlasY = 0; atlasY < sprite.AtlasHeight; atlasY++)
        {
            var localY = atlasY % sprite.Height;
            for (var atlasX = 0; atlasX < sprite.AtlasWidth; atlasX++)
            {
                var pixel = (atlasY * sprite.AtlasWidth + atlasX) * 4;
                var alpha = rgba[pixel + 3] / 255.0;
                if (alpha <= 0)
                    continue;

                var red = rgba[pixel] / 255.0;
                var green = rgba[pixel + 1] / 255.0;
                var blue = rgba[pixel + 2] / 255.0;
                var luminance = red * 0.2126 + green * 0.7152 + blue * 0.0722;
                var weight = alpha * luminance;
                if (weight <= 0)
                    continue;

                weightSum += weight;
                xSum += (atlasX % sprite.Width + 0.5) * weight;
                ySum += (localY + 0.5) * weight;
                redSum += red * weight;
                greenSum += green * weight;
                blueSum += blue * weight;
            }
        }

        if (weightSum <= double.Epsilon)
            return null;

        var colour = new Vector3(
            (float)(redSum / weightSum),
            (float)(greenSum / weightSum),
            (float)(blueSum / weightSum));
        var strongestChannel = MathF.Max(colour.X, MathF.Max(colour.Y, colour.Z));
        if (strongestChannel > 0)
            colour /= strongestChannel;

        return new WorldLightAppearance(
            (float)(xSum / weightSum),
            (float)(ySum / weightSum),
            colour,
            HaloOpacity);
    }
}

internal readonly record struct WorldLightAppearance(
    float CenterX,
    float CenterY,
    Vector3 Colour,
    float Opacity);
