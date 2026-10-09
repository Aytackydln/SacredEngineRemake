using System.Numerics;
using ImGuiNET;
using Sacred.Engine.Rendering;
using Sacred.World.Map;

namespace Sacred.Engine.Graphics.ImGui;

internal static class ImGuiWorldMapPanel
{
    public static void Build(WorldMapFrame frame, int width, int height, ImFontPtr mapFont)
    {
        if (frame.Controls is not { } controls) return;
        if (controls.RegionNamesVisible) ImGuiWorldMapLabels.Draw(frame, width, height, mapFont);
        if (!controls.DebugPanelVisible) return;
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.10f, 0.105f, 0.075f, 0.94f));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.19f, 0.17f, 0.10f, 1));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.26f, 0.22f, 0.13f, 1));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.58f, 0.49f, 0.29f, 0.7f));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.89f, 0.84f, 0.67f, 1));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.24f, 0.24f, 0.16f, 1));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0.88f, 0.72f, 0.36f, 1));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.35f, 0.30f, 0.18f, 1));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.34f, 0.32f, 0.20f, 1));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.40f, 0.35f, 0.21f, 1));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.30f, 0.18f, 1));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.45f, 0.38f, 0.22f, 1));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.53f, 0.43f, 0.24f, 1));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.45f, 0.38f, 0.22f, 1));
        ImGuiNET.ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.53f, 0.43f, 0.24f, 1));
        ImGuiNET.ImGui.SetNextWindowPos(new Vector2(16, 16), ImGuiCond.FirstUseEver);
        ImGuiNET.ImGui.SetNextWindowSize(new Vector2(260, 0), ImGuiCond.FirstUseEver);
        if (ImGuiNET.ImGui.Begin("World map", ImGuiWindowFlags.AlwaysAutoResize))
        {
            var selected = (int)controls.SelectedMap;
            if (ImGuiNET.ImGui.Combo("Map", ref selected, "Main game (Ancaria)\0Underworld\0"))
                controls.SelectedMap = (WorldMapKind)selected;
            var names = controls.RegionNamesVisible;
            if (ImGuiNET.ImGui.Checkbox("Region names", ref names))
            {
                controls.RegionNamesVisible = names;
                EngineLog.WriteLine($"Debug input: map region names {(names ? "visible" : "hidden")}.");
            }
            var npcs = controls.RegionNpcsVisible;
            if (ImGuiNET.ImGui.Checkbox("Region NPCs", ref npcs))
            {
                controls.RegionNpcsVisible = npcs;
                EngineLog.WriteLine($"Debug input: map region NPCs {(npcs ? "visible" : "hidden")}.");
            }
            ImGuiNET.ImGui.Separator();
            if (ImGuiNET.ImGui.Button("Fit map")) controls.FitRequested = true;
            ImGuiNET.ImGui.TextDisabled("Wheel: zoom   Right-drag: pan");
        }
        ImGuiNET.ImGui.End();
        ImGuiNET.ImGui.PopStyleColor(15);
    }
}
