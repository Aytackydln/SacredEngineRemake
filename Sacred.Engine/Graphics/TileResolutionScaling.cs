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
    public const int MinimumStepPercentage = 1;
    public const int MaximumStepPercentage = 100;
    
    public const int DefaultMinimumPercentage = 75;
    public const int DefaultMaximumPercentage = 200;
    public const int DefaultStepPercentage = 25;

    public const int TileHeight = IsometricProjection.StepHeight; // 48
    public const int ReferenceTileHeightCount = 16;
    public const int ReferenceResolutionY = TileHeight * ReferenceTileHeightCount; // 768

    public static int CalculateRenderHeight(
        float zoom,
        int outputHeight,
        int minimumPercentage,
        int maximumPercentage,
        bool snapToPercentageSteps,
        int stepPercentage)
    {
        var desiredHeight = !float.IsFinite(zoom) || zoom <= 0.0f
            ? ReferenceResolutionY
            : Math.Max(1, (int)MathF.Round(ReferenceResolutionY / zoom));
        if (snapToPercentageSteps)
        {
            stepPercentage = Math.Clamp(stepPercentage, MinimumStepPercentage, MaximumStepPercentage);
            var minimumStepPercentage = (int)MathF.Ceiling(minimumPercentage / (float)stepPercentage) * stepPercentage;
            var maximumStepPercentage = (int)MathF.Floor(maximumPercentage / (float)stepPercentage) * stepPercentage;
            // Keep the configured limits when the range contains no complete step.
            if (minimumStepPercentage <= maximumStepPercentage)
            {
                minimumPercentage = minimumStepPercentage;
                maximumPercentage = maximumStepPercentage;
            }

            var desiredPercentage = desiredHeight * 100.0f / Math.Max(1, outputHeight);
            var snappedPercentage = MathF.Round(
                desiredPercentage / stepPercentage,
                MidpointRounding.AwayFromZero) * stepPercentage;
            desiredHeight = Math.Max(1, (int)MathF.Round(outputHeight * snappedPercentage / 100.0f));
        }

        var minimumHeight = Math.Max(1, (int)MathF.Round(outputHeight * minimumPercentage / 100.0f));
        var maximumHeight = Math.Max(minimumHeight, (int)MathF.Round(outputHeight * maximumPercentage / 100.0f));

        return Math.Clamp(desiredHeight, minimumHeight, maximumHeight);
    }
}
