using System;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

internal static class ImGuiCampaignPanel
{
    public static void Draw(DebugUiControlState controls)
    {
        DearImGui.Text($"Current: {controls.CampaignDisplayName}");
        DearImGui.BeginDisabled(!controls.CampaignChangeAvailable);
        DearImGui.Text("Campaign / multiplayer");
        DearImGui.SetNextItemWidth(-1.0f);
        if (DearImGui.BeginCombo("##campaign-selection", controls.CampaignDisplayName))
        {
            foreach (var campaign in controls.Campaigns)
            {
                var selected = campaign.DirectoryPath.Equals(controls.CampaignDirectoryPath,
                    StringComparison.OrdinalIgnoreCase);
                if (DearImGui.Selectable(campaign.Name, selected))
                    controls.RequestedCampaign = campaign.DirectoryPath;
                if (selected) DearImGui.SetItemDefaultFocus();
            }
            DearImGui.EndCombo();
        }
        if (DearImGui.Button("Reload current campaign") && controls.CampaignDirectoryPath is { } path)
            controls.RequestedCampaign = path;
        DearImGui.SameLine();
        if (DearImGui.Button("Refresh list")) controls.RequestedCampaignListRefresh = true;
        DearImGui.EndDisabled();
        DearImGui.TextWrapped("Changing selection reloads the world. Character choice and position are kept; scene state resets.");
    }
}
