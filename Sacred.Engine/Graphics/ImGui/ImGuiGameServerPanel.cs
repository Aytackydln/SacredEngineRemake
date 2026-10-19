using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Sacred.World.Networking;
using DearImGui = ImGuiNET.ImGui;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Manual participant join and visible progress for the verified native protocol.</summary>
internal static class ImGuiGameServerPanel
{
    public static void Draw(GameServerDebugPanelState panel)
    {
        var viewport = DearImGui.GetMainViewport();
        var size = new Vector2(MathF.Min(610, MathF.Max(300, viewport.WorkSize.X - 24)),
            MathF.Min(720, MathF.Max(200, viewport.WorkSize.Y - 160)));
        DearImGui.SetNextWindowSize(size, ImGuiCond.FirstUseEver);
        DearImGui.SetNextWindowPos(viewport.WorkPos + new Vector2(MathF.Max(12, MathF.Min(590, viewport.WorkSize.X - size.X - 12)), 148), ImGuiCond.FirstUseEver);
        var open = panel.Visible;
        var contents = DearImGui.Begin("GameServer", ref open);
        panel.Visible = open;
        if (!contents) { DearImGui.End(); return; }

        DearImGui.TextWrapped("Join the original GameServer as a participant. Keep the original client in its started game.");
        ImGuiGameServerDiscoveryPanel.Draw(panel);
        DearImGui.BeginDisabled(panel.Connected || panel.Joining);
        DearImGui.SetNextItemWidth(-1);
        DearImGui.InputText("##server-endpoint", ref panel.Endpoint, 256);
        DearImGui.TextDisabled("TCP host:port (local server: 127.0.0.1:2006)");
        var selected = panel.Heroes.FirstOrDefault(hero => hero.Selection == panel.Selection);
        DearImGui.SetNextItemWidth(-1);
        if (DearImGui.BeginCombo("##server-hero", selected?.Label ?? "Select saved hero"))
        {
            foreach (var hero in panel.Heroes)
            {
                DearImGui.BeginDisabled(hero.Error is not null);
                if (DearImGui.Selectable(hero.Label, hero.Selection == panel.Selection)) panel.Selection = hero.Selection;
                DearImGui.EndDisabled();
                if (hero.Error is not null && DearImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    DearImGui.SetTooltip(hero.Error);
            }
            DearImGui.EndCombo();
        }
        DearImGui.BeginDisabled(selected is null || selected.Error is not null || string.IsNullOrWhiteSpace(panel.Endpoint));
        if (DearImGui.Button("Join game")) panel.JoinRequested = true;
        DearImGui.EndDisabled();
        DearImGui.SameLine();
        if (DearImGui.Button("Refresh saved heroes")) panel.RefreshHeroesRequested = true;
        DearImGui.EndDisabled();
        DearImGui.SameLine();
        DearImGui.BeginDisabled(!panel.Connected && !panel.Joining);
        if (DearImGui.Button(panel.Joining ? "Cancel join" : "Disconnect")) panel.DisconnectRequested = true;
        DearImGui.EndDisabled();
        DearImGui.TextDisabled(panel.HeroStatus);
        if (DearImGui.CollapsingHeader("Game files")) DearImGui.TextWrapped(panel.GameDirectory);

        DearImGui.Separator();
        DearImGui.TextColored(panel.WorldReady ? new(0.4f, 1, 0.5f, 1) : new(1, 0.8f, 0.4f, 1),
            panel.WorldReady ? $"World ready - player slot {panel.PlayerSlot}" : panel.Connected ? "Connected - preparing world" : panel.Joining ? "Joining..." : "Disconnected");
        DearImGui.TextWrapped(panel.JoinStatus);
        if (!string.IsNullOrEmpty(panel.ActionStatus)) DearImGui.TextWrapped(panel.ActionStatus);
        DearImGui.TextWrapped(panel.WorldStatus);
        var world = panel.Replicas;
        if (world is not null)
        {
            DearImGui.Text($"Received {panel.EventsReceived} events / {panel.OpcodeCounts.Count} message types");
            DearImGui.Text($"Replicas: {world.Objects.Count} objects / {world.Inventories.Count} inventories");
            DearImGui.Text($"Sectors: {world.Sectors.Count} confirmed / {world.SectorInterest.Requested.Count} requested; center {world.SectorInterest.Center}");
            if (world.LocalPlayerSlot != 0 && world.Objects.TryGetValue(world.LocalPlayerSlot, out var hero))
            {
                var position = SacredNetworkPosition.ToTiles(hero.BaseState);
                DearImGui.Text($"Your hero: tiles {position.X:0.###}, {position.Y:0.###}; sector {hero.BaseState.Sector}; surface {hero.BaseState.Surface}");
            }
        }

        if (DearImGui.CollapsingHeader("Movement")) DrawWalking(panel);
        if (DearImGui.CollapsingHeader("Received objects")) ImGuiGameServerObjectsPanel.Draw(panel);
        if (DearImGui.CollapsingHeader("Protocol diagnostics")) DrawDiagnostics(panel);
        if (DearImGui.CollapsingHeader("Current support", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DearImGui.TextWrapped("Controls: left click to navigate or select a door, F / gamepad A to interact, WASD / arrows / gamepad to move, Shift to walk. Doors use received state and normal approach movement. Creatures use received facing, locomotion animation and equipped models.");
            DearImGui.TextWrapped("Pending: combat, pickups, script execution, mounted movement and indoor navigation. Movement currently uses the remake's base speeds; native stat modifiers remain pending.");
        }
        DearImGui.End();
    }

    private static void DrawWalking(GameServerDebugPanelState panel)
    {
        DearImGui.TextWrapped("Use left click, WASD / arrows or gamepad in the world. This target test uses the same navigation path.");
        DearImGui.BeginDisabled(!panel.WorldReady || panel.Walking);
        DearImGui.InputFloat2("Navigate to tiles", ref panel.WalkTarget);
        if (DearImGui.Button("Navigate")) panel.NavigateRequested = true;
        DearImGui.SameLine();
        if (DearImGui.Button("Stop navigation")) panel.StopNavigationRequested = true;
        DearImGui.EndDisabled();
        DearImGui.TextWrapped(panel.MovementStatus);
        if (!DearImGui.CollapsingHeader("Advanced packet patrol")) return;
        DearImGui.TextWrapped("Outdoor route inside the current sector. Supply the native movement control from a packet sample; speed is explicit until native stat mapping is complete.");
        DearImGui.BeginDisabled(!panel.WorldReady || panel.Walking);
        DearImGui.InputFloat2("Target tiles", ref panel.WalkTarget);
        if (DearImGui.Button("Use current position") && panel.Replicas is { } world && world.Objects.TryGetValue(world.LocalPlayerSlot, out var hero))
            panel.WalkTarget = SacredNetworkPosition.ToTiles(hero.BaseState);
        DearImGui.InputText("Native control (decimal)", ref panel.WalkControl, 32);
        DearImGui.InputFloat("Tiles / second", ref panel.WalkSpeed);
        DearImGui.Checkbox("Repeat there and back", ref panel.Patrol);
        DearImGui.BeginDisabled(!uint.TryParse(panel.WalkControl, out _) || !float.IsFinite(panel.WalkSpeed) || panel.WalkSpeed <= 0);
        if (DearImGui.Button("Walk route")) panel.WalkRequested = true;
        DearImGui.EndDisabled();
        DearImGui.EndDisabled();
        DearImGui.SameLine();
        DearImGui.BeginDisabled(!panel.Walking);
        if (DearImGui.Button("Stop walking")) panel.StopWalkRequested = true;
        DearImGui.EndDisabled();
        DearImGui.TextWrapped(panel.WalkStatus);
    }

    private static void DrawDiagnostics(GameServerDebugPanelState panel)
    {
        DearImGui.TextWrapped(panel.ConnectionStatus);
        if (panel.Replicas is { } world)
            DearImGui.TextWrapped($"Retained scripts: {world.DeathScripts.Registrations.Count} death registrations; variables: {world.ScriptVariables.Records.Count} received / {world.ScriptVariables.Count} slots; triggers: {world.MovementTriggers.Sectors.Count} sector banks, {world.MovementTriggers.RemovedRecordCount} records removed / {world.MovementTriggerDeletions.Instructions.Count} pending routing; statistics: {world.Statistics.States.Count} snapshots; world banks: {world.WorldBanks.States.Count}.");
        DearImGui.TextDisabled("Write details to the console:");
        foreach (var diagnostic in new[] { "status", "sectors", "objects", "inventories", "scripts", "statistics", "banks" })
        {
            if (DearImGui.SmallButton(diagnostic)) panel.DiagnosticsRequested.Enqueue(diagnostic);
            DearImGui.SameLine();
        }
        DearImGui.NewLine();
        if (DearImGui.BeginChild("##server-opcodes", new Vector2(0, 140), ImGuiChildFlags.Borders))
            foreach (var entry in panel.OpcodeCounts.OrderBy(entry => entry.Key))
                DearImGui.Text($"0x{entry.Key:X4}: {entry.Value} events");
        DearImGui.EndChild();
    }
}
