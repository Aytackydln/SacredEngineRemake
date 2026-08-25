using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Assets.Paks.Texture;

namespace Sacred.Engine.Rendering;

/// <summary>
/// Derives the position and tint of the texture-backed halo from the actual
/// animated mini-object atlas. Its size remains authored in Items.pak.
/// </summary>
internal sealed class AnimatedSpriteHaloAppearanceCache
{
    public const float HaloOpacity = 0.24f;

    private readonly Dictionary<StaticSpriteAsset, AnimatedSpriteHaloAppearance?> _appearances =
        new(ReferenceEqualityComparer.Instance);

    public bool TryGet(StaticSpriteAsset sprite, out AnimatedSpriteHaloAppearance appearance)
    {
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

    private static AnimatedSpriteHaloAppearance? DeriveAppearance(StaticSpriteAsset sprite)
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

        return new AnimatedSpriteHaloAppearance(
            (float)(xSum / weightSum),
            (float)(ySum / weightSum),
            colour);
    }
}

internal readonly record struct AnimatedSpriteHaloAppearance(
    float CenterX,
    float CenterY,
    Vector3 Colour);
