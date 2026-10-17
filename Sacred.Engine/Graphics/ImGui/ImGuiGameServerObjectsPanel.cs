using System.Linq;
using System.Numerics;
using ImGuiNET;
using Sacred.World.Networking;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

internal static class ImGuiGameServerObjectsPanel
{
    private static string _filter = string.Empty;
    private static bool _creaturesOnly = true;

    public static void Draw(GameServerDebugPanelState panel)
    {
        if (panel.Replicas is not { } world) return;
        DearImGui.Checkbox("Creatures only", ref _creaturesOnly);
        DearImGui.InputText("Reference filter", ref _filter, 32);
        DearImGui.TextDisabled("Current reported positions; up to 100 matching replicas.");
        if (DearImGui.BeginChild("##server-objects", new Vector2(0, 220), ImGuiChildFlags.Borders))
        {
            foreach (var obj in world.Objects.Values.Where(obj => (!_creaturesOnly || obj.Creature is not null) &&
                         obj.BaseState.ObjectReference.ToString().Contains(_filter)).OrderBy(obj => obj.BaseState.ObjectReference).Take(100))
            {
                var state = obj.BaseState;
                var position = SacredNetworkPosition.ToTiles(state);
                DearImGui.Text($"Ref {state.ObjectReference}  type {state.ObjectType}  owner {state.Owner}  sector {state.Sector}");
                DearImGui.TextDisabled($"  tiles {position.X:0.###}, {position.Y:0.###}  surface {state.Surface}  {(obj.Movement is null ? "stationary" : "moving")}");
                if (obj.MountPartnerReference is > 0) DearImGui.TextDisabled($"  Mount partner {obj.MountPartnerReference}");
            }
        }
        DearImGui.EndChild();
    }
}
