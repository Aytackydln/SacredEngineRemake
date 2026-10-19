using ImGuiNET;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

internal static class ImGuiGameServerDiscoveryPanel
{
    public static void Draw(GameServerDebugPanelState panel)
    {
        DearImGui.TextUnformatted("LAN games");
        DearImGui.TextWrapped(panel.DiscoveryStatus);
        DearImGui.SetNextItemWidth(100);
        DearImGui.InputInt("UDP port", ref panel.DiscoveryPort, 0);
        DearImGui.BeginDisabled(panel.DiscoveryPort is < 1 or > ushort.MaxValue);
        if (DearImGui.Button("Refresh LAN games")) panel.RefreshDiscoveryRequested = true;
        DearImGui.EndDisabled();
        if (panel.Servers.Count == 0)
            DearImGui.TextDisabled("Waiting for GameServer announcements...");
        for (var index = 0; index < panel.Servers.Count; index++)
        {
            var server = panel.Servers[index];
            DearImGui.PushID(index);
            DearImGui.BeginDisabled(panel.Connected || panel.Joining);
            if (DearImGui.Selectable($"{server.Name}  ({server.Players}/{server.MaximumPlayers})  {server.Endpoint}",
                    panel.Endpoint == server.Endpoint.ToString()))
                panel.Endpoint = server.Endpoint.ToString();
            DearImGui.EndDisabled();
            if (DearImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                DearImGui.SetTooltip($"Version 0x{server.Version:X4}, flags 0x{server.Flags:X8}");
            DearImGui.PopID();
        }
        DearImGui.Separator();
    }
}
