using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Queues local progression edits through the ordinary game update.</summary>
internal sealed class ImGuiPlayerCombatPanel
{
    private int _level = 1;
    private ushort _observedLevel;
    private uint _character;

    public void Draw(DebugUiControlState controls, PlayerDebugPanelState player)
    {
        if (_character != player.SelectedCharacterEntryId || _observedLevel != player.Level)
        { _character = player.SelectedCharacterEntryId; _observedLevel = player.Level; _level = player.Level; }
        DearImGui.TextDisabled("Combat testing");
        DearImGui.SetNextItemWidth(120);
        DearImGui.InputInt("Player level", ref _level, 1, 10);
        DearImGui.SameLine();
        DearImGui.BeginDisabled(_level is < 1 or > ushort.MaxValue);
        if (DearImGui.Button("Apply level")) controls.RequestedPlayerLevel = _level;
        DearImGui.EndDisabled();
        if (DearImGui.Button("Set all CAs to level 1")) controls.RequestedAllCombatArtsLevelOne = true;
        if (DearImGui.IsItemHovered()) DearImGui.SetTooltip("Sets base ranks to 1. Equipment bonuses still apply.");
        DearImGui.Text($"Learned arts: {player.LearnedCombatArts}/{player.AvailableCombatArts}");
        DearImGui.TextWrapped("Assign an art to a combat quickslot. Right-click casts toward the pointer; Ctrl+left-click attacks.");
        DearImGui.TextWrapped(player.CombatStatus);
        DearImGui.Separator();
    }
}
