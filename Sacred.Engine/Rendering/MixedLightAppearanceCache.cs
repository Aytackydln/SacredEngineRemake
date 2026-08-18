using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Assets.Paks.Texture;

namespace Sacred.Engine.Rendering;

/// <summary>
/// Derives a local light source from the authored blue-white pixels inside a
/// LICHTER mixed sprite. The surrounding fixture remains an ordinary static
/// sprite and is never used to position or colour the emitted light.
/// </summary>
internal sealed class MixedLightAppearanceCache
{
    private const float LightOpacity = 0.035f;
    private readonly Dictionary<StaticSpriteAsset, MixedLightAppearance?> _appearances =
        new(ReferenceEqualityComparer.Instance);

    public bool TryGet(StaticSpriteAsset sprite, out MixedLightAppearance appearance)
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

    private static MixedLightAppearance? DeriveAppearance(StaticSpriteAsset sprite)
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
        var left = sprite.Width;
        var top = sprite.Height;
        var right = 0;
        var bottom = 0;

        for (var y = 0; y < sprite.Height; y++)
        for (var x = 0; x < sprite.Width; x++)
        {
            var pixel = (y * sprite.AtlasWidth + x) * 4;
            var alpha = rgba[pixel + 3] / 255.0;
            var red = rgba[pixel] / 255.0;
            var green = rgba[pixel + 1] / 255.0;
            var blue = rgba[pixel + 2] / 255.0;
            var blueSignal = Math.Max(0.0, blue - red - 0.04);
            var whiteSignal = Math.Max(0.0, Math.Max(red, Math.Max(green, blue)) - 0.80);
            var weight = alpha * Math.Max(blueSignal, whiteSignal);
            if (weight <= 0.01)
                continue;

            weightSum += weight;
            xSum += (x + 0.5) * weight;
            ySum += (y + 0.5) * weight;
            redSum += red * weight;
            greenSum += green * weight;
            blueSum += blue * weight;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x + 1);
            bottom = Math.Max(bottom, y + 1);
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

        var luminousExtent = Math.Max(right - left, bottom - top);
        var diameter = Math.Clamp(luminousExtent * 2.4f, 64.0f, 144.0f);
        return new MixedLightAppearance(
            (float)(xSum / weightSum),
            (float)(ySum / weightSum),
            top,
            diameter,
            Math.Clamp(luminousExtent * 1.15f, 34.0f, 52.0f),
            colour,
            LightOpacity);
    }
}

internal readonly record struct MixedLightAppearance(
    float CenterX,
    float CenterY,
    float EmitterTop,
    float Diameter,
    float SparkleDiameter,
    Vector3 Colour,
    float Opacity);
