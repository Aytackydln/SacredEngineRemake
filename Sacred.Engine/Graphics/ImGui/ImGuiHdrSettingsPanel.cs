using System;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Groups HDR luminance and effect intensity controls with their reset actions.</summary>
internal static class ImGuiHdrSettingsPanel
{
    public static void Draw(Dx12DeviceContext graphics)
    {
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

}
