using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Assets.Paks.Texture;

namespace Sacred.Engine.Rendering;

/// <summary>Checks every texel reachable by the terrain shader's rounded diamond sampling.</summary>
internal static class TerrainTileOpacity
{
    // Match SacredTerrainTileCommon.hlsl. Transparent padding outside this polygon
    // is never sampled. Include any texel whose rounding square intersects it,
    // so the check remains conservative when elevation changes rasterization.
    private static readonly Vector2[] SourceCorners =
    [new(2.512f, 24.012f), new(50.512f, 1.012f), new(98.012f, 23.500f), new(50.000f, 48.512f)];
    private static readonly (int X, int Y)[] SampledTexels = CreateSampledTexels();

    public static bool IsOpaque(TextureAsset texture, int sourceX, int sourceY)
    {
        var pixels = texture.Rgba8;
        if (pixels.Length == 0) return false;
        foreach (var (x, y) in SampledTexels)
        {
            // The shader clamps source coordinates to the sheet edges.
            var offset = (Math.Clamp(sourceY + y, 0, texture.Height - 1) * texture.Width +
                          Math.Clamp(sourceX + x, 0, texture.Width - 1)) * 4 + 3;
            if (pixels[offset] is not 0 and not byte.MaxValue) return false;
        }
        return true;
    }

    private static (int X, int Y)[] CreateSampledTexels()
    {
        var texels = new List<(int, int)>();
        for (var y = 1; y <= 49; y++)
        {
            var lower = y - 0.5f;
            var upper = y + 0.5f;
            var minX = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            for (var edge = 0; edge < SourceCorners.Length; edge++)
            {
                var start = SourceCorners[edge];
                var end = SourceCorners[(edge + 1) % SourceCorners.Length];
                if (start.Y >= lower && start.Y <= upper) Include(start.X);
                Intersect(lower);
                Intersect(upper);

                void Intersect(float rowY)
                {
                    var t = (rowY - start.Y) / (end.Y - start.Y);
                    if (t >= 0 && t <= 1) Include(start.X + t * (end.X - start.X));
                }
            }
            for (var x = (int)MathF.Ceiling(minX - 0.5f); x <= MathF.Floor(maxX + 0.5f); x++)
                texels.Add((x, y));

            void Include(float x)
            {
                minX = MathF.Min(minX, x);
                maxX = MathF.Max(maxX, x);
            }
        }
        return texels.ToArray();
    }
}
