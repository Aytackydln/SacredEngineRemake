using System;
using Sacred.Engine.Scene.InGame;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Builds controls for gameplay cheats owned by the engine runtime.</summary>
internal static class ImGuiCheatsPanel
{
    public static void Draw(DebugUiControlState controls)
    {
        DearImGui.SetNextItemWidth(180.0f);
        if (DearImGui.BeginCombo("Collision", controls.CollisionMode.ToString()))
        {
            foreach (var mode in Enum.GetValues<CollisionCheatMode>())
            {
                var selected = mode == controls.CollisionMode;
                if (DearImGui.Selectable(mode.ToString(), selected))
                {
                    controls.RequestedCollisionMode = mode;
                    EngineLog.WriteLine($"Debug input: collision set to {mode}");
                }

                if (selected)
                    DearImGui.SetItemDefaultFocus();
            }

            DearImGui.EndCombo();
        }

        DearImGui.TextDisabled("Console: set collision <walk|fly|noclip>");
    }
}
