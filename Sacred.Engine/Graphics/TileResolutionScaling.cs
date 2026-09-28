using System;
using Sacred.World.Geometry;

namespace Sacred.Engine.Graphics;

/// <summary>
/// Calculates 1:1 pixel-to-texel render resolutions and scaling percentages
/// based on world tile dimensions and camera zoom level.
/// </summary>
public static class TileResolutionScaling
{
    public const int MinimumPercentage = 25;
    public const int MaximumPercentage = 300;

    public const int TileHeight = IsometricProjection.StepHeight; // 48
    public const int ReferenceTileHeightCount = 16;
    public const int ReferenceResolutionY = TileHeight * ReferenceTileHeightCount; // 768

    public static int CalculateRenderHeight(float zoom)
    {
        if (!float.IsFinite(zoom) || zoom <= 0.0f)
            return ReferenceResolutionY;

        return Math.Max(1, (int)MathF.Round(ReferenceResolutionY / zoom));
    }
}
