using System;
using Sacred.Assets.Paks.Texture;

namespace Sacred.Engine.Rendering;

/// <summary>Creates the translucent diamond used by the F9 WLDX collision view.</summary>
internal sealed class BlockedAreaDebugTileSourceFactory
{
    private const int Width = 100;
    private const int Height = 50;

    public TerrainTileSource Source { get; } = new(
        new TextureAsset("$path-blocked-debug", Width, Height, CreatePixels()),
        0,
        0);

    private static byte[] CreatePixels()
    {
        var pixels = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var distance = MathF.Abs((x + 0.5f - Width * 0.5f) / (Width * 0.5f)) +
                           MathF.Abs((y + 0.5f - Height * 0.5f) / (Height * 0.5f));
            if (distance > 1.0f)
                continue;

            var edge = distance > 0.86f;
            var offset = (y * Width + x) * 4;
            pixels[offset + 0] = 255;
            pixels[offset + 1] = edge ? (byte)215 : (byte)45;
            pixels[offset + 2] = edge ? (byte)70 : (byte)35;
            pixels[offset + 3] = edge ? (byte)225 : (byte)105;
        }

        return pixels;
    }
}
