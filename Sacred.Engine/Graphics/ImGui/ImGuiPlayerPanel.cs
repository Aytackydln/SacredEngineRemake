using System.Linq;
using System.Numerics;
using ImGuiNET;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Owns the player window, equipment presets and offline combat previews.</summary>
internal sealed class ImGuiPlayerPanel
{
    private readonly ImGuiPlayerParticlesPanel _playerParticles = new();
    private readonly ImGuiPlayerCombatPanel _playerCombat = new();

    public void Draw(DebugUiControlState controls, Dx12ImGuiRenderer renderer)
    {
        var player = controls.Player;
        if (player is null)
            return;

        DearImGui.SetNextWindowSize(new Vector2(500.0f, 640.0f), ImGuiCond.FirstUseEver);
        DearImGui.SetNextWindowPos(new Vector2(590.0f, 120.0f), ImGuiCond.FirstUseEver);
        var open = controls.PlayerPanelVisible;
        DearImGui.PushFont(renderer.TitleFont);
        var drawContents = DearImGui.Begin("Player", ref open);
        DearImGui.PopFont();
        controls.PlayerPanelVisible = open;
        if (!drawContents)
        {
            DearImGui.End();
            return;
        }

        DearImGui.PushFont(renderer.BodyFont);
        DearImGui.Text($"Character: {player.CharacterName}");
        var networkActive = controls.GameServer.Connected || controls.GameServer.Joining;
        if (networkActive) DearImGui.TextWrapped("Offline character controls are available after disconnecting from GameServer.");
        DearImGui.BeginDisabled(networkActive);
        _playerCombat.Draw(controls, player);
        _playerParticles.Draw(controls);
        DearImGui.Separator();
        if (DearImGui.CollapsingHeader("Character presets", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var preset in player.CharacterPresets)
            {
                DearImGui.BeginDisabled(preset.EntryId == player.SelectedCharacterEntryId);
                if (DearImGui.Button($"Load {preset.DisplayName}##character-{preset.EntryId}"))
                    controls.RequestedPlayerCharacter = preset.EntryId;
                DearImGui.EndDisabled();
            }
        }

        DearImGui.Separator();
        DearImGui.TextDisabled("Equipment");
        DearImGui.SameLine();
        DearImGui.BeginDisabled(!player.EquipmentSlots.Any(static slot => slot.EquipmentItemId.HasValue));
        if (DearImGui.SmallButton("Remove all##equipment"))
            controls.RequestedPlayerRemoveAllEquipment = true;
        DearImGui.EndDisabled();
        foreach (var slot in player.EquipmentSlots)
        {
            DearImGui.Text($"{slot.SlotName}: {slot.EquipmentName ?? "Empty"}");
            if (slot.EquipmentName is null)
                continue;

            DearImGui.SameLine();
            if (DearImGui.SmallButton($"Remove##equipment-{slot.SlotIndex}"))
                controls.RequestedPlayerEquipmentRemoval = slot.SlotIndex;
            DearImGui.SameLine();
            DearImGui.TextDisabled($"#{slot.EquipmentItemId}");
        }

        DearImGui.Separator();
        DearImGui.TextDisabled("Item sets");
        foreach (var set in player.ItemSets)
        {
            var label = $"{set.SetName}###set-{set.SetIndex}";
            DearImGui.BeginDisabled(set.ResolvedEquipmentCount == 0);
            if (DearImGui.Button(label))
                controls.RequestedPlayerItemSet = set.SetIndex;
            DearImGui.EndDisabled();
            DearImGui.SameLine();
            DearImGui.Text($"ID {set.SetIdentifier}  {set.ResolvedEquipmentCount}/{set.ItemCount} items");
        }

        DearImGui.EndDisabled();
        DearImGui.PopFont();
        DearImGui.End();
    }

}
