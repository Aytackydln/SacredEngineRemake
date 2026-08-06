using System;
using System.Collections.Generic;
using Sacred.Assets.Paks.Texture;

namespace Sacred.Engine.Rendering;

/// <summary>Creates 100x50 source diamonds for the stairs-zone debug layer.</summary>
internal sealed class StairsDebugTileSourceFactory
{
    private const int Width = 100;
    private const int Height = 50;

    private readonly Dictionary<bool, TerrainTileSource> _sources = [];

    public TerrainTileSource Get(bool isAnchor)
    {
        if (_sources.TryGetValue(isAnchor, out var source))
            return source;

        var texture = new TextureAsset(
            $"$stairs-debug-{(isAnchor ? "anchor" : "cell")}",
            Width,
            Height,
            CreatePixels(isAnchor));
        source = new TerrainTileSource(texture, 0, 0);
        _sources.Add(isAnchor, source);
        return source;
    }

    private static byte[] CreatePixels(bool isAnchor)
    {
        var pixels = new byte[Width * Height * 4];
        var color = (R: (byte)35, G: (byte)190, B: (byte)255);
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var diamondDistance = MathF.Abs((x + 0.5f - Width * 0.5f) / (Width * 0.5f)) +
                                  MathF.Abs((y + 0.5f - Height * 0.5f) / (Height * 0.5f));
            if (diamondDistance > 1.0f)
                continue;

            var edge = diamondDistance > 0.84f;
            var offset = (y * Width + x) * 4;
            pixels[offset + 0] = edge || isAnchor ? (byte)255 : color.R;
            pixels[offset + 1] = edge || isAnchor ? (byte)255 : color.G;
            pixels[offset + 2] = edge || isAnchor ? (byte)255 : color.B;
            pixels[offset + 3] = edge ? (byte)230 : isAnchor ? (byte)145 : (byte)100;
        }

        return pixels;
    }
}
