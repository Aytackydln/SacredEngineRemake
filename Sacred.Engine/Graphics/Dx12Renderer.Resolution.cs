using System;

namespace Sacred.Engine.Graphics;

public sealed partial class Dx12Renderer
{
    private int _manualResolutionPercentage = 100;
    private float _resolutionZoom = 1.0f;

    public void SetRenderResolutionPercentage(int percentage)
    {
        AutoRenderResolution = false;
        _manualResolutionPercentage = Math.Clamp(percentage,
            TileResolutionScaling.MinimumPercentage, TileResolutionScaling.MaximumPercentage);
        UpdateRenderResolution();
    }

    public void SetAutoRenderResolution(bool enabled)
    {
        if (AutoRenderResolution && !enabled)
            _manualResolutionPercentage = (int)MathF.Round(_graphics.RenderResolutionPercentage * 100);
        AutoRenderResolution = enabled;
        UpdateRenderResolution();
    }

    public void SetAutoRenderResolutionRange(int minimumPercentage, int maximumPercentage)
    {
        minimumPercentage = Math.Clamp(
            minimumPercentage,
            TileResolutionScaling.MinimumPercentage,
            TileResolutionScaling.MaximumPercentage);
        maximumPercentage = Math.Clamp(
            maximumPercentage,
            TileResolutionScaling.MinimumPercentage,
            TileResolutionScaling.MaximumPercentage);
        AutoRenderResolutionMinimumPercentage = Math.Min(minimumPercentage, maximumPercentage);
        AutoRenderResolutionMaximumPercentage = Math.Max(minimumPercentage, maximumPercentage);
    }

    public void SetAutoRenderResolutionStepSnapping(bool enabled, int stepPercentage)
    {
        AutoRenderResolutionStepSnapping = enabled;
        AutoRenderResolutionStepPercentage = Math.Clamp(
            stepPercentage,
            TileResolutionScaling.MinimumStepPercentage,
            TileResolutionScaling.MaximumStepPercentage);
    }

    public void UpdateAutoRenderResolution(float zoom)
    {
        _resolutionZoom = zoom;
        if (AutoRenderResolution) UpdateRenderResolution();
    }

    private void HandleOutputResize()
    {
        UpdateRenderResolution();
        ResetFsr2History();
    }

    private void UpdateRenderResolution()
    {
        if (OutputHeight <= 0) return;
        var height = AutoRenderResolution
            ? TileResolutionScaling.CalculateRenderHeight(
                _resolutionZoom, OutputHeight,
                AutoRenderResolutionMinimumPercentage, AutoRenderResolutionMaximumPercentage,
                AutoRenderResolutionStepSnapping, AutoRenderResolutionStepPercentage)
            : Math.Max(1, (int)MathF.Round(OutputHeight * _manualResolutionPercentage / 100.0f));
        var width = Math.Max(1, (int)MathF.Round(height * (float)OutputWidth / OutputHeight));
        _graphics.SetRenderResolution(width, height);
    }

}
