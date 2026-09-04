using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Builds controls for gameplay cheats owned by the engine runtime.</summary>
internal static class ImGuiCheatsPanel
{
    public static void Draw(DebugUiControlState controls)
    {
        var noClipEnabled = controls.NoClipEnabled;
        if (DearImGui.Checkbox("No collision (noclip)", ref noClipEnabled))
        {
            controls.RequestedNoClipEnabled = noClipEnabled;
            EngineLog.WriteLine($"Debug input: noclip {(noClipEnabled ? "enabled" : "disabled")}");
        }

        DearImGui.TextDisabled("Console: noclip [on|off]");
    }
}
