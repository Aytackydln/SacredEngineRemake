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
    public const int MaximumPercentage = 200;

    /// <summary>
    /// Tile diamond width in pixels (96).
    /// </summary>
    public const int TileWidth = IsometricProjection.StepWidth;

    /// <summary>
    /// Tile diamond height in pixels (48).
    /// </summary>
    public const int TileHeight = IsometricProjection.StepHeight;

    /// <summary>
    /// Calculates the render resolution percentage (clamped to 25%..200%) that achieves
    /// exact 1-to-1 pixel-to-texel terrain tile rendering for a given camera zoom level.
    /// </summary>
    public static int CalculateRenderResolutionPercentage(float zoom)
    {
        if (!float.IsFinite(zoom) || zoom <= 0.0f)
            return 100;

        return Math.Clamp((int)MathF.Round(100.0f / zoom), MinimumPercentage, MaximumPercentage);
    }

    /// <summary>
    /// Calculates the exact render width and height for 1-to-1 tile pixel rendering.
    /// </summary>
    public static (int Width, int Height) CalculateExactResolution(int outputWidth, int outputHeight, float zoom)
    {
        if (!float.IsFinite(zoom) || zoom <= 0.0f)
            return (Math.Max(1, outputWidth), Math.Max(1, outputHeight));

        var percentage = CalculateRenderResolutionPercentage(zoom);
        var width = Math.Max(1, (int)MathF.Round(outputWidth * percentage / 100.0f));
        var height = Math.Max(1, (int)MathF.Round(outputHeight * percentage / 100.0f));
        return (width, height);
    }

    /// <summary>
    /// Calculates the camera zoom level that corresponds to 1:1 tile rendering at the given percentage.
    /// </summary>
    public static float CalculateZoomForPercentage(int percentage)
    {
        if (percentage <= 0)
            return 1.0f;

        return 100.0f / percentage;
    }
}
