using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sacred.Engine.Graphics.ImGui;
using Sacred.Engine.Multiplayer;
using Sacred.Network.Admission;

namespace Sacred.Engine;

internal sealed partial class SacredGameRuntime
{
    private Task<IReadOnlyList<GameServerSavedHeroChoice>>? _serverHeroChoices;
    private DateTime _nextServerUiUpdate;
    private readonly GameServerDiscoveryController _serverDiscovery = new();

    private bool TrySetServerPanelOption(string option, string value, out string message)
    {
        message = string.Empty;
        if (option.Equals("server-discovery", StringComparison.OrdinalIgnoreCase))
        {
            message = _serverDiscovery.SetOption(value, _gameDirectory, _debugUiControls.GameServer);
            return true;
        }
        if (!option.Equals("server-panel", StringComparison.OrdinalIgnoreCase)) return false;
        if (TryParseBoolean(value, out var visible))
        {
            _debugUiControls.GameServer.Visible = visible;
            message = $"GameServer panel {(visible ? "opened" : "closed")}";
        }
        else message = "Use set server-panel <on|off>.";
        return true;
    }

    private void ApplyGameServerDebugRequests()
    {
        var panel = _debugUiControls.GameServer;
        if (panel.RefreshHeroesRequested && _serverHeroChoices is null)
        {
            panel.RefreshHeroesRequested = false;
            panel.HeroStatus = "Reading saved heroes...";
            _serverHeroChoices = Task.Run(() => GameServerSavedHeroCatalog.Read(_gameDirectory));
        }
        if (panel.DisconnectRequested)
        {
            panel.DisconnectRequested = false;
            panel.JoinRequested = false;
            panel.WalkRequested = false;
            _cheats.Submit("set server disconnect");
        }
        if (panel.JoinRequested)
        {
            panel.JoinRequested = false;
            var previousJoin = _serverJoin;
            if (TrySetServerJoinOption("server-join", $"{panel.Endpoint.Trim()} {panel.Selection} {GoldConnectionModes.Argument(panel.ConnectionMode)}", out var message))
            {
                panel.ActionStatus = ReferenceEquals(previousJoin, _serverJoin) ? message : string.Empty;
                EngineLog.WriteLine($"Debug input: GameServer join: {message}");
            }
        }
        if (panel.StopWalkRequested)
        {
            panel.StopWalkRequested = false;
            _cheats.Submit("set server-walk stop");
        }
        if (panel.NavigateRequested)
        {
            panel.NavigateRequested = false;
            TrySetServerMoveOption("server-move", FormattableString.Invariant($"{panel.WalkTarget.X},{panel.WalkTarget.Y}"), out var message);
            panel.ActionStatus = message;
            EngineLog.WriteLine($"Debug input: {message}");
        }
        if (panel.StopNavigationRequested)
        {
            panel.StopNavigationRequested = false;
            TrySetServerMoveOption("server-move", "stop", out var message);
            panel.ActionStatus = message;
            EngineLog.WriteLine($"Debug input: {message}");
        }
        if (panel.WalkRequested)
        {
            panel.WalkRequested = false;
            var target = panel.WalkTarget;
            var value = FormattableString.Invariant($"{(panel.Patrol ? "patrol " : string.Empty)}{target.X},{target.Y} {panel.WalkControl.Trim()} {panel.WalkSpeed}");
            TrySetServerWalkOption("server-walk", value, out var message);
            panel.ActionStatus = message;
            EngineLog.WriteLine($"Debug input: GameServer walk: {message}");
        }
        while (panel.DiagnosticsRequested.TryDequeue(out var diagnostic))
            _cheats.Submit("set server " + diagnostic);
    }

    private void UpdateGameServerDebugUi()
    {
        var panel = _debugUiControls.GameServer;
        _serverDiscovery.Update(_gameDirectory, panel);
        if (_serverHeroChoices is { IsCompleted: true } choices)
        {
            _serverHeroChoices = null;
            try
            {
                panel.Heroes = choices.GetAwaiter().GetResult();
                var available = panel.Heroes.Where(hero => hero.Error is null).ToArray();
                if (!available.Any(hero => hero.Selection == panel.Selection))
                    panel.Selection = available.FirstOrDefault()?.Selection ?? 0;
                panel.HeroStatus = $"{available.Length} saved heroes available.";
                EngineLog.WriteLine($"Network loading complete: {panel.HeroStatus}");
            }
            catch (Exception error)
            {
                panel.HeroStatus = "Saved hero listing failed: " + error.Message;
                EngineLog.WriteLine(panel.HeroStatus);
            }
        }
        var wasConnected = panel.Connected;
        var wasReady = panel.WorldReady;
        var previousStatus = panel.JoinStatus;
        panel.GameDirectory = _gameDirectory;
        panel.Connected = _gameServer.IsConnected;
        panel.Joining = _serverJoin is { IsFinished: false };
        panel.WorldReady = _inGameScene?.NetworkWorldReady == true;
        panel.Walking = _serverWalk is { IsComplete: false };
        panel.PlayerSlot = panel.Connected ? _gameServer.PlayerSlot : (byte)0;
        panel.JoinStatus = _serverJoin?.Status ?? "Disconnected. Select a saved hero and join the running game.";
        if (!panel.Connected && _serverJoin is { IsFinished: true } && !panel.JoinStatus.StartsWith("failed:", StringComparison.Ordinal))
            panel.JoinStatus = "Disconnected. Select a saved hero and join the running game.";
        panel.WalkStatus = _serverWalk?.Status ?? "No walk requested.";
        panel.MovementStatus = _inGameScene?.NetworkMovementStatus ?? "Movement waits for the local actor.";
        panel.Replicas = _gameServer.WorldReplicas;
        // The UI reads replicas on the game thread. Throttle string construction and counter snapshots.
        if (DateTime.UtcNow < _nextServerUiUpdate && wasConnected == panel.Connected && wasReady == panel.WorldReady && previousStatus == panel.JoinStatus) return;
        _nextServerUiUpdate = DateTime.UtcNow.AddMilliseconds(250);
        panel.ConnectionStatus = _gameServer.Status;
        panel.WorldStatus = _inGameScene?.NetworkWorldStatus ?? "in-game scene is not loaded";
        panel.EventsReceived = _gameServer.EventsReceived;
        panel.OpcodeCounts = _gameServer.OpcodeCounts.ToDictionary(entry => entry.Key, entry => entry.Value);
    }
}
