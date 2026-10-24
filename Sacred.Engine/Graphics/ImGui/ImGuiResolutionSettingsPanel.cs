using static Sacred.Engine.Graphics.ImGui.ImGuiGraphicsPanel;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Groups manual and automatic resolution with their reconstruction filter.</summary>
internal static class ImGuiResolutionSettingsPanel
{
    public static void Draw(DebugUiControlState controls)
    {
        Checkbox("Auto resolution (1:1 tiles)", controls.AutoRenderResolution,
            value =>
            {
                controls.RequestedAutoRenderResolution = value;
                EngineLog.WriteLine($"Debug input: auto render resolution set to {(value ? "enabled" : "disabled")}");
            });
        if (controls.AutoRenderResolution)
        {
            var minimum = controls.AutoRenderResolutionMinimumPercentage;
            var maximum = controls.AutoRenderResolutionMaximumPercentage;
            DearImGui.SetNextItemWidth(260.0f);
            if (DearImGui.SliderInt(
                    "Auto resolution minimum",
                    ref minimum,
                    TileResolutionScaling.MinimumPercentage,
                    maximum,
                    "%d%%"))
            {
                controls.RequestedAutoRenderResolutionRange = (minimum, maximum);
            }
            if (DearImGui.IsItemDeactivatedAfterEdit())
                EngineLog.WriteLine($"Debug input: auto render resolution minimum set to {minimum}%");

            DearImGui.SetNextItemWidth(260.0f);
            if (DearImGui.SliderInt(
                    "Auto resolution maximum",
                    ref maximum,
                    minimum,
                    TileResolutionScaling.MaximumPercentage,
                    "%d%%"))
            {
                controls.RequestedAutoRenderResolutionRange = (minimum, maximum);
            }
            if (DearImGui.IsItemDeactivatedAfterEdit())
                EngineLog.WriteLine($"Debug input: auto render resolution maximum set to {maximum}%");

            Checkbox("Snap auto resolution to steps", controls.AutoRenderResolutionStepSnapping,
                value => controls.RequestedAutoRenderResolutionStepSnapping =
                    (value, controls.AutoRenderResolutionStepPercentage));
            if (controls.AutoRenderResolutionStepSnapping)
            {
                var stepPercentage = controls.AutoRenderResolutionStepPercentage;
                DearImGui.SetNextItemWidth(260.0f);
                if (DearImGui.SliderInt(
                        "Auto resolution step",
                        ref stepPercentage,
                        TileResolutionScaling.MinimumStepPercentage,
                        TileResolutionScaling.MaximumStepPercentage,
                        "%d%%"))
                {
                    controls.RequestedAutoRenderResolutionStepSnapping = (true, stepPercentage);
                }
                if (DearImGui.IsItemDeactivatedAfterEdit())
                    EngineLog.WriteLine($"Debug input: auto render resolution step set to {stepPercentage}%");

                DearImGui.TextDisabled("Rounds automatic resolution to percentage multiples to stabilize terrain sampling.");
            }
        }
        var resolution = controls.RenderResolutionPercentage;
        if (controls.AutoRenderResolution)
        {
            DearImGui.BeginDisabled();
            DearImGui.SliderInt("Render resolution", ref resolution, TileResolutionScaling.MinimumPercentage, TileResolutionScaling.MaximumPercentage, "%d%% (auto 1:1)");
            DearImGui.EndDisabled();
        }
        else
        {
            if (DearImGui.SliderInt("Render resolution", ref resolution, TileResolutionScaling.MinimumPercentage, TileResolutionScaling.MaximumPercentage, "%d%%"))
            {
                controls.RequestedRenderResolutionPercentage = resolution;
                EngineLog.WriteLine($"Debug input: render resolution set to {resolution}%");
            }
        }
        EnumCombo("Render scaling", controls.RenderScalingMode,
            value => controls.RequestedRenderScalingMode = value, FormatRenderScalingMode);
        if (controls.RenderScalingMode == RenderScalingMode.Fsr2Lanczos2)
            DearImGui.TextDisabled("FSR 2 below window resolution; Lanczos2 above window resolution.");
    }

    private static string FormatRenderScalingMode(RenderScalingMode mode) => mode switch
    {
        RenderScalingMode.Fsr1 => "FSR 1 (spatial)",
        RenderScalingMode.Fsr1MotionAdaptive => "FSR 1 (motion-adaptive sharpen)",
        RenderScalingMode.Fsr2 => "FSR 2 (temporal)",
        RenderScalingMode.Lanczos2 => "Lanczos2 downsampling",
        RenderScalingMode.Fsr2Lanczos2 => "FSR 2 / Lanczos2 (auto)",
        _ => mode.ToString()
    };
}
