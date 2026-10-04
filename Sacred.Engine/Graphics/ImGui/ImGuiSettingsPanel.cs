using System;
using System.Collections.Generic;
using Sacred.Engine.Graphics.Skinning;
using Sacred.Engine.Latency;
using Sacred.Engine.Scene.InGame;
using Sacred.Particles;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Builds engine-wide F-key controls and HDR luminance settings.</summary>
internal static class ImGuiSettingsPanel
{
    public static void Draw(Dx12DeviceContext graphics, DebugUiControlState controls)
    {
        DearImGui.TextDisabled("F-key controls");
        Checkbox("HDR output (F4)", controls.HdrEnabled,
            value => controls.RequestedHdrEnabled = value);
        EnumCombo(
            "Frame pacing (F5)",
            controls.FramePacingMode,
            value => controls.RequestedFramePacingMode = value,
            FormatFramePacingMode);
        if (controls.FramePacingMode == FramePacingMode.Manual)
        {
            var manualFrameRate = controls.ManualFrameRate;
            DearImGui.SetNextItemWidth(260.0f);
            if (DearImGui.SliderInt(
                    "Manual FPS",
                    ref manualFrameRate,
                    FramePacingController.MinimumManualFrameRate,
                    FramePacingController.MaximumManualFrameRate,
                    "%d FPS"))
            {
                controls.RequestedManualFrameRate = manualFrameRate;
            }

            if (DearImGui.IsItemDeactivatedAfterEdit())
                EngineLog.WriteLine($"Debug input: manual frame rate set to {manualFrameRate} FPS");
        }
        EnumCombo(
            "Low latency (F6)",
            controls.LowLatencyMode,
            value => controls.RequestedLowLatencyMode = value,
            FormatLowLatencyMode);
        EnumCombo(
            "World lighting (F7)",
            controls.WorldLightingMode,
            value => controls.RequestedWorldLightingMode = value,
            FormatWorldLightingMode);
        DrawSunAngleControls();
        Checkbox("Borderless fullscreen (F10)", controls.BorderlessFullscreen,
            value => controls.RequestedBorderlessFullscreen = value);
        EnumCombo(
            "Particle effects",
            controls.ParticleQuality,
            value => controls.RequestedParticleQuality = value,
            FormatParticleQuality);
        Checkbox("GPU-only (disable CPU fallbacks)",
            controls.SkinningMode == SkinningMode.GpuOnly && controls.ParticleSimulation == ParticleSimulationMode.GpuOnly,
            value =>
            {
                controls.RequestedSkinningMode = value ? SkinningMode.GpuOnly : SkinningMode.Auto;
                controls.RequestedParticleSimulation = value ? ParticleSimulationMode.GpuOnly : ParticleSimulationMode.Auto;
            });
        DearImGui.TextDisabled("GPU-only skips unavailable GPU work. Pose sampling, births and lifetime accounting still use CPU.");
        EnumCombo("Model skinning", controls.SkinningMode,
            value => controls.RequestedSkinningMode = value,
            mode => mode == SkinningMode.GpuOnly ? "GPU only (no fallback)" : mode.ToString());
        EnumCombo("Particle simulation", controls.ParticleSimulation,
            value => controls.RequestedParticleSimulation = value,
            mode => mode switch { ParticleSimulationMode.Auto => "Auto (prefer GPU)", ParticleSimulationMode.CpuSimd => "CPU (SIMD)", ParticleSimulationMode.Gpu => "GPU", ParticleSimulationMode.GpuOnly => "GPU only (no fallback)", _ => "CPU (scalar)" });
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
        if (DearImGui.Button("Capture screenshot (F12)"))
        {
            controls.ScreenshotRequested = true;
            EngineLog.WriteLine("Debug input: screenshot requested from ImGui");
        }

        DearImGui.Separator();
        DearImGui.TextDisabled("HDR brightness");
        DearImGui.TextDisabled("Frame brightness sets output white. Color multipliers affect HDR RGB before blending.");

        var settings = graphics.HdrBrightnessSettings;
        var scene = settings.SceneBrightnessNits;
        var unlit = settings.UnlitColorMultiplier;
        var particles = settings.ParticleColorMultiplier;
        var changed = false;

        changed |= BrightnessControl(
            "Frame brightness", "scene-brightness", ref scene,
            40.0f, 500.0f, HdrBrightnessSettings.DefaultSceneBrightnessNits, graphics);
        changed |= ColorMultiplierControl("Unlit objects", ref unlit, HdrBrightnessSettings.DefaultUnlitColorMultiplier);
        changed |= ColorMultiplierControl("Particles", ref particles, HdrBrightnessSettings.DefaultParticleColorMultiplier);

        if (DearImGui.Button("Reset HDR brightness"))
        {
            scene = HdrBrightnessSettings.DefaultSceneBrightnessNits;
            unlit = HdrBrightnessSettings.DefaultUnlitColorMultiplier;
            particles = HdrBrightnessSettings.DefaultParticleColorMultiplier;
            changed = true;
            EngineLog.WriteLine("Debug input: HDR brightness and color multipliers reset to defaults");
        }

        if (changed)
        {
            graphics.SetHdrBrightnessSettings(settings with
            {
                SceneBrightnessNits = scene,
                UnlitColorMultiplier = unlit,
                ParticleColorMultiplier = particles
            });
        }
    }

    private static bool ColorMultiplierControl(string label, ref float value, float defaultValue)
    {
        DearImGui.AlignTextToFramePadding();
        DearImGui.TextUnformatted(label);
        DearImGui.SameLine(180.0f);
        DearImGui.SetNextItemWidth(245.0f);
        var changed = DearImGui.SliderFloat($"##{label}-multiplier", ref value, 0.0f, 4.0f, "%.2fx");
        var editFinished = DearImGui.IsItemDeactivatedAfterEdit();
        DearImGui.SameLine();
        if (DearImGui.SmallButton($"Reset##{label}-multiplier"))
        {
            value = defaultValue;
            changed = true;
            editFinished = true;
        }
        if (editFinished)
            EngineLog.WriteLine($"Debug input: {label} color multiplier set to {value:0.##}x");
        return changed;
    }

    private static bool BrightnessControl(
        string label,
        string id,
        ref float value,
        float minimum,
        float maximum,
        float defaultValue,
        Dx12DeviceContext graphics)
    {
        var osWhite = graphics.GetOsSdrWhiteLevel();
        DearImGui.AlignTextToFramePadding();
        DearImGui.TextUnformatted(label);
        DearImGui.SameLine(180.0f);
        DearImGui.SetNextItemWidth(245.0f);
        var changed = DearImGui.SliderFloat($"##{id}", ref value, minimum,
            Math.Max(maximum, osWhite.IsAvailable ? osWhite.Nits : maximum), "%.0f nits");
        var editFinished = DearImGui.IsItemDeactivatedAfterEdit();
        DearImGui.SetCursorPosX(180.0f);
        if (DearImGui.SmallButton($"Reset 160##{id}"))
        {
            value = defaultValue;
            changed = true;
            EngineLog.WriteLine($"Debug input: {label} reset to {value:0} nits");
        }
        else if (editFinished)
        {
            EngineLog.WriteLine($"Debug input: {label} set to {value:0} nits");
        }

        DearImGui.SameLine();
        DearImGui.BeginDisabled(!osWhite.IsAvailable);
        if (DearImGui.SmallButton($"Reset OS##{id}"))
        {
            osWhite = graphics.GetOsSdrWhiteLevel(refresh: true);
            if (osWhite.IsAvailable)
            {
                value = osWhite.Nits;
                changed = true;
                EngineLog.WriteLine($"Debug input: {label} reset to {value:0.##} nits from {osWhite.Source}");
            }
        }
        DearImGui.EndDisabled();
        DearImGui.TextDisabled(osWhite.IsAvailable
            ? $"{osWhite.Source}: {osWhite.Nits:0.##} nits"
            : $"OS SDR white unavailable. Reset 160 remains available.");

        return changed;
    }

    private static void DrawSunAngleControls()
    {
        DearImGui.TextDisabled("Sun direction");
        var azimuth = SolarLightingCalculator.SunAzimuthDegrees;
        var elevation = SolarLightingCalculator.SunElevationDegrees;
        var changed = false;

        changed |= DearImGui.SliderFloat("Sun azimuth", ref azimuth, -180.0f, 180.0f, "%.2f deg");
        changed |= DearImGui.SliderFloat("Sun elevation", ref elevation, -89.0f, 89.0f, "%.2f deg");
        if (changed)
        {
            SolarLightingCalculator.SunAzimuthDegrees = azimuth;
            SolarLightingCalculator.SunElevationDegrees = elevation;
            EngineLog.WriteLine($"Debug input: sun angles set to azimuth {azimuth:0.00}°, elevation {elevation:0.00}°");
        }

        if (DearImGui.SmallButton("Reset sun angles"))
        {
            SolarLightingCalculator.SunAzimuthDegrees = SolarLightingCalculator.DefaultSunAzimuthDegrees;
            SolarLightingCalculator.SunElevationDegrees = SolarLightingCalculator.DefaultSunElevationDegrees;
            EngineLog.WriteLine("Debug input: sun angles reset to the previous fixed direction");
        }
    }

    private static void Checkbox(string label, bool current, Action<bool> setter)
    {
        if (!DearImGui.Checkbox(label, ref current))
            return;

        setter(current);
        EngineLog.WriteLine($"Debug input: {label} {(current ? "enabled" : "disabled")}");
    }

    private static void EnumCombo<TEnum>(
        string label,
        TEnum current,
        Action<TEnum> setter,
        Func<TEnum, string> formatter)
        where TEnum : struct, Enum
    {
        DearImGui.SetNextItemWidth(260.0f);
        if (!DearImGui.BeginCombo(label, formatter(current)))
            return;

        foreach (var value in Enum.GetValues<TEnum>())
        {
            var selected = EqualityComparer<TEnum>.Default.Equals(value, current);
            if (DearImGui.Selectable(formatter(value), selected))
            {
                setter(value);
                EngineLog.WriteLine($"Debug input: {label} set to {formatter(value)}");
            }
            if (selected)
                DearImGui.SetItemDefaultFocus();
        }
        DearImGui.EndCombo();
    }

    private static string FormatFramePacingMode(FramePacingMode mode) => mode switch
    {
        FramePacingMode.VariableRefreshRate => "Variable refresh rate",
        FramePacingMode.VSync => "VSync",
        FramePacingMode.MonitorRefreshLimiter => "Monitor refresh limiter",
        FramePacingMode.Manual => "Manual",
        _ => mode.ToString()
    };

    private static string FormatLowLatencyMode(LowLatencyMode mode) => mode switch
    {
        LowLatencyMode.OnPlusBoost => "On + Boost",
        _ => mode.ToString()
    };

    private static string FormatWorldLightingMode(WorldLightingMode mode) => mode switch
    {
        WorldLightingMode.TimedDayNightCycle => "Timed day/night cycle",
        WorldLightingMode.PitchBlack => "Pitch black",
        _ => mode.ToString()
    };

    private static string FormatParticleQuality(SacredParticleQuality quality) => quality switch
    {
        SacredParticleQuality.Low => "Low",
        SacredParticleQuality.Medium => "Medium",
        SacredParticleQuality.High => "High",
        _ => quality.ToString()
    };

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
