using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Assets.Paks.Texture;

namespace Sacred.Engine.Rendering;

/// <summary>
/// Derives a local glow from authored blue-white pixels in a class-9 mixed
/// sprite. Numeric Items.pak fields select candidates; pixel evidence prevents
/// ordinary class-9 props from being treated as emitters.
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
        double blueWeightSum = 0;
        var bluePixelCount = 0;
        var brightBluePixelCount = 0;
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
            var blueSignal = blue > 0.40
                ? Math.Max(0.0, Math.Min(blue - red - 0.12, blue - green - 0.04))
                : 0.0;
            var weight = alpha * blueSignal;
            if (weight <= 0.01)
                continue;

            blueWeightSum += weight;
            bluePixelCount++;
            if (blue > 0.65 && blue - red > 0.20 && blue - green > 0.06)
                brightBluePixelCount++;

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

        // Real embedded emitters have a compact core dominated by saturated,
        // bright blue pixels. Large blue materials such as quest flags have
        // many weak-blue cloth pixels but only sparse bright highlights.
        if (weightSum <= double.Epsilon ||
            blueWeightSum <= 1.0 ||
            bluePixelCount < 16 ||
            brightBluePixelCount < 16 ||
            brightBluePixelCount * 4 < bluePixelCount)
        {
            return null;
        }

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
