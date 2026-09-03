using System;
using System.Linq;
using System.Numerics;
using Sacred.Assets.Paks.Texture;

namespace Sacred.Engine.Rendering;

/// <summary>Creates fully transparent topology guides for WLDX terrain diamonds.</summary>
internal sealed class TerrainTopologyDebugTileSourceFactory
{
    private const int Width = 100;
    private const int Height = 50;

    public TerrainTileSource Source { get; } = new(
        new TextureAsset(
            "$terrain-topology-native-quad",
            Width,
            Height,
            CreatePixels()),
        0,
        0);

    private static byte[] CreatePixels()
    {
        var pixels = new byte[Width * Height * 4];
        var left = new Vector2(2.5f, 24.0f);
        var top = new Vector2(50.5f, 1.0f);
        var right = new Vector2(98.0f, 23.5f);
        var bottom = new Vector2(50.0f, 48.5f);
        var edges = new[] { (left, top), (top, right), (right, bottom), (bottom, left) };

        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var point = new Vector2(x + 0.5f, y + 0.5f);
            var isCorner = Distance(point, left) <= 2.4f || Distance(point, top) <= 2.4f ||
                           Distance(point, right) <= 2.4f || Distance(point, bottom) <= 2.4f;
            var isEdge = edges.Any(segment => DistanceToSegment(point, segment.Item1, segment.Item2) <= 0.9f);
            var isDiagonal = DistanceToSegment(point, top, bottom) <= 0.75f;
            if (!isCorner && !isEdge && !isDiagonal)
                continue;

            var offset = (y * Width + x) * 4;
            var color = isCorner ? (R: (byte)255, G: (byte)215, B: (byte)35)
                : isDiagonal ? (R: (byte)255, G: (byte)70, B: (byte)210)
                : (R: (byte)30, G: (byte)230, B: (byte)255);
            pixels[offset + 0] = color.R;
            pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.B;
            pixels[offset + 3] = isCorner ? (byte)255 : (byte)235;
        }

        return pixels;
    }

    private static float Distance(Vector2 left, Vector2 right) => Vector2.Distance(left, right);

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= float.Epsilon)
            return Distance(point, start);
        var amount = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0.0f, 1.0f);
        return Distance(point, start + segment * amount);
    }
}
