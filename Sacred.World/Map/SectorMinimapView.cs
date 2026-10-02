using System.Numerics;
using Sacred.Core.World.Sector;
using Sacred.World.Geometry;
using Sacred.World.Rendering;

namespace Sacred.World.Map;

/// <summary>The rectangular world crop used by an authored MINIMAP texture.</summary>
public sealed record SectorMinimapView(Vector2 Center, int RenderWidth, int RenderHeight, float Zoom)
{
    public static SectorMinimapView Create(SectorCoord sector, int textureWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureWidth);
        // The minimap's right edge anchors the sector center. Its rectangle spans
        // one sector step horizontally and two sector steps vertically.
        var center = new Vector2((sector.X + 0.25f) * Sector.TileCount,
                                 (sector.Y + 0.75f) * Sector.TileCount);
        var zoom = textureWidth / (Sector.TileCount * IsometricProjection.StepWidth * 0.5f);
        return new SectorMinimapView(center, textureWidth, textureWidth, zoom);
    }

    public static RgbaImage ResizeHeight(RgbaImage source, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (height < source.Height)
            return RgbaImageDownsampler.Resize(source, source.Width, height);
        if (source.Height == height) return source;
        var pixels = new byte[checked(source.Width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            var sourceY = Math.Min(source.Height - 1, (int)((y + 0.5) * source.Height / height));
            source.Pixels.AsSpan(sourceY * source.Stride, source.Stride).CopyTo(pixels.AsSpan(y * source.Stride));
        }
        return new RgbaImage(source.Width, height, pixels);
    }
}
