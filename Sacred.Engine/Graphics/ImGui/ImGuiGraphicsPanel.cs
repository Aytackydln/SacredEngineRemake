using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Sacred.Engine.Graphics.Skinning;
using Sacred.Engine.Latency;
using Sacred.Engine.Scene.InGame;
using Sacred.Particles;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Owns the independent graphics window and its grouped rendering controls.</summary>
internal static class ImGuiGraphicsPanel
{
    public static void Draw(Dx12DeviceContext graphics, DebugUiControlState controls, Dx12ImGuiRenderer renderer)
    {
        DearImGui.SetNextWindowSize(new Vector2(620.0f, 720.0f), ImGuiCond.FirstUseEver);
        DearImGui.SetNextWindowSizeConstraints(new Vector2(580.0f, 320.0f), new Vector2(float.MaxValue));
        DearImGui.SetNextWindowPos(new Vector2(590.0f, 148.0f), ImGuiCond.FirstUseEver);
        var open = controls.GraphicsPanelVisible;
        DearImGui.PushFont(renderer.TitleFont);
        var drawContents = DearImGui.Begin("Graphics", ref open);
        DearImGui.PopFont();
        if (controls.GraphicsPanelVisible != open)
            EngineLog.WriteLine("Debug input: graphics panel closed");
        controls.GraphicsPanelVisible = open;
        if (drawContents)
        {
            DearImGui.PushFont(renderer.BodyFont);
            DearImGui.PushItemWidth(260.0f);
            DearImGui.PushTextWrapPos(0.0f);
            if (DearImGui.CollapsingHeader("Display", ImGuiTreeNodeFlags.DefaultOpen))
                DrawDisplay(controls);
            if (DearImGui.CollapsingHeader("Resolution & scaling", ImGuiTreeNodeFlags.DefaultOpen))
                ImGuiResolutionSettingsPanel.Draw(controls);
            if (DearImGui.CollapsingHeader("Frame pacing & latency", ImGuiTreeNodeFlags.DefaultOpen))
                DrawFramePacing(controls);
            if (DearImGui.CollapsingHeader("HDR brightness & color"))
                ImGuiHdrSettingsPanel.Draw(graphics);
            if (DearImGui.CollapsingHeader("World lighting"))
                DrawWorldLighting(controls);
            if (DearImGui.CollapsingHeader("Particles & animation"))
                DrawEffects(controls);
            if (DearImGui.CollapsingHeader("World streaming & cache"))
                DrawStreaming(controls);
            DearImGui.Separator();
            if (DearImGui.Button("Capture screenshot (F12)"))
            {
                controls.ScreenshotRequested = true;
                EngineLog.WriteLine("Debug input: screenshot requested from ImGui");
            }
            DearImGui.PopTextWrapPos();
            DearImGui.PopItemWidth();
            DearImGui.PopFont();
        }
        DearImGui.End();
    }

    private static void DrawDisplay(DebugUiControlState controls)
    {
        Checkbox("Borderless fullscreen (F10)", controls.BorderlessFullscreen,
            value => controls.RequestedBorderlessFullscreen = value);
        Checkbox("HDR output (F4)", controls.HdrEnabled,
            value => controls.RequestedHdrEnabled = value);
    }

    private static void DrawFramePacing(DebugUiControlState controls)
    {
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
    }

    private static void DrawWorldLighting(DebugUiControlState controls)
    {
        EnumCombo(
            "World lighting (F7)",
            controls.WorldLightingMode,
            value => controls.RequestedWorldLightingMode = value,
            FormatWorldLightingMode);
        DrawSunAngleControls();
    }

    private static void DrawEffects(DebugUiControlState controls)
    {
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
            mode => mode switch
            {
                ParticleSimulationMode.Auto => "Auto (prefer GPU)",
                ParticleSimulationMode.CpuSimd => "CPU (SIMD)",
                ParticleSimulationMode.Gpu => "GPU",
                ParticleSimulationMode.GpuOnly => "GPU only (no fallback)",
                _ => "CPU (scalar)"
            });
    }

    private static void DrawStreaming(DebugUiControlState controls)
    {
        EnumCombo("Loaded sectors", controls.SectorLoadMode,
            value => controls.RequestedSectorLoadMode = value, mode => $"{(int)mode} sectors");
        Checkbox("Wait for sector GPU uploads at load", controls.WaitForSectorGpuUploads,
            value => controls.RequestedWaitForSectorGpuUploads = value);
        Checkbox("Cache tight static cutouts (uses VRAM)", controls.StaticCutoutsEnabled,
            value => controls.RequestedStaticCutoutsEnabled = value);
        if (controls.StaticCutoutsEnabled)
        {
            var budget = controls.StaticCutoutBudgetMiB;
            if (DearImGui.SliderInt("Static cutout cache", ref budget, 0, 1024, "%d MiB"))
                controls.RequestedStaticCutoutBudgetMiB = budget;
        }
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

    internal static void Checkbox(string label, bool current, Action<bool> setter)
    {
        if (!DearImGui.Checkbox(label, ref current))
            return;

        setter(current);
        EngineLog.WriteLine($"Debug input: {label} {(current ? "enabled" : "disabled")}");
    }

    internal static void EnumCombo<TEnum>(
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
}
