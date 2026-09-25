using System;
using System.Collections.Generic;
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
        Checkbox("Auto resolution (1:1 tiles)", controls.AutoRenderResolution,
            value =>
            {
                controls.RequestedAutoRenderResolution = value;
                EngineLog.WriteLine($"Debug input: auto render resolution set to {(value ? "enabled" : "disabled")}");
            });
        var resolution = controls.RenderResolutionPercentage;
        if (controls.AutoRenderResolution)
        {
            DearImGui.BeginDisabled();
            DearImGui.SliderInt("Render resolution", ref resolution, 25, TileResolutionScaling.MaximumPercentage, "%d%% (auto 1:1)");
            DearImGui.EndDisabled();
        }
        else
        {
            if (DearImGui.SliderInt("Render resolution", ref resolution, 25, TileResolutionScaling.MaximumPercentage, "%d%%"))
            {
                controls.RequestedRenderResolutionPercentage = resolution;
                EngineLog.WriteLine($"Debug input: render resolution set to {resolution}%");
            }
        }
        EnumCombo("Render scaling", controls.RenderScalingMode,
            value => controls.RequestedRenderScalingMode = value, FormatRenderScalingMode);
        if (DearImGui.Button("Capture screenshot (F12)"))
        {
            controls.ScreenshotRequested = true;
            EngineLog.WriteLine("Debug input: screenshot requested from ImGui");
        }

        DearImGui.Separator();
        DearImGui.TextDisabled("HDR brightness");
        DearImGui.TextDisabled("Brightness changes are applied to HDR output immediately.");

        var settings = graphics.HdrBrightnessSettings;
        var scene = settings.SceneBrightnessNits;
        var ui = settings.UiBrightnessNits;
        var diffuse = settings.SunDiffuseNits;
        var specular = settings.SunSpecularNits;
        var unlitSprites = settings.UnlitSpriteNits;
        var changed = false;

        changed |= BrightnessControl(
            "Scene brightness", "scene-brightness", ref scene,
            40.0f, 500.0f, HdrBrightnessSettings.DefaultSceneBrightnessNits);
        changed |= BrightnessControl(
            "UI brightness", "ui-brightness", ref ui,
            40.0f, 1_000.0f, HdrBrightnessSettings.DefaultUiBrightnessNits);
        changed |= BrightnessControl(
            "Sun diffuse", "sun-diffuse", ref diffuse,
            40.0f, 2_000.0f, HdrBrightnessSettings.DefaultSunDiffuseNits);
        changed |= BrightnessControl(
            "Highlights", "sun-specular", ref specular,
            40.0f, 4_000.0f, HdrBrightnessSettings.DefaultSunSpecularNits);
        changed |= BrightnessControl(
            "Unlit sprites / halos", "unlit-sprites", ref unlitSprites,
            40.0f, 2_000.0f, HdrBrightnessSettings.DefaultUnlitSpriteNits);

        if (DearImGui.Button("Reset all HDR brightness"))
        {
            scene = HdrBrightnessSettings.DefaultSceneBrightnessNits;
            ui = HdrBrightnessSettings.DefaultUiBrightnessNits;
            diffuse = HdrBrightnessSettings.DefaultSunDiffuseNits;
            specular = HdrBrightnessSettings.DefaultSunSpecularNits;
            unlitSprites = HdrBrightnessSettings.DefaultUnlitSpriteNits;
            changed = true;
            EngineLog.WriteLine("Debug input: all HDR brightness settings reset to defaults");
        }

        if (changed)
        {
            graphics.SetHdrBrightnessSettings(new HdrBrightnessSettings
            {
                SceneBrightnessNits = scene,
                UiBrightnessNits = ui,
                SunDiffuseNits = diffuse,
                SunSpecularNits = specular,
                UnlitSpriteNits = unlitSprites
            });
        }
    }

    private static bool BrightnessControl(
        string label,
        string id,
        ref float value,
        float minimum,
        float maximum,
        float defaultValue)
    {
        DearImGui.AlignTextToFramePadding();
        DearImGui.TextUnformatted(label);
        DearImGui.SameLine(180.0f);
        DearImGui.SetNextItemWidth(245.0f);
        var changed = DearImGui.SliderFloat($"##{id}", ref value, minimum, maximum, "%.0f nits");
        var editFinished = DearImGui.IsItemDeactivatedAfterEdit();
        DearImGui.SameLine();
        if (DearImGui.SmallButton($"Reset##{id}"))
        {
            value = defaultValue;
            changed = true;
            EngineLog.WriteLine($"Debug input: {label} reset to {value:0} nits");
        }
        else if (editFinished)
        {
            EngineLog.WriteLine($"Debug input: {label} set to {value:0} nits");
        }

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
        RenderScalingMode.Fsr2 => "FSR 2 (temporal)",
        _ => mode.ToString()
    };
}
