using System;
using System.Collections.Generic;
using Sacred.Engine.Latency;
using Sacred.Engine.Scene.InGame;
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
        Checkbox("Borderless fullscreen (F10)", controls.BorderlessFullscreen,
            value => controls.RequestedBorderlessFullscreen = value);
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
            "Sun specular", "sun-specular", ref specular,
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
}
