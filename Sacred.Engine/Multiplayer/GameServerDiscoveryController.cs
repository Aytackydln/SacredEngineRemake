using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using Sacred.Engine.Graphics.ImGui;
using Sacred.Network.Discovery;

namespace Sacred.Engine.Multiplayer;

/// <summary>Owns LAN listening and publishes discovery state to the game-thread UI.</summary>
internal sealed class GameServerDiscoveryController : IDisposable
{
    private GoldLanDiscoveryListener? _listener;
    private bool _initialized;
    private bool _enabled;
    private bool _attempted;

    public void Update(string gameDirectory, GameServerDebugPanelState panel)
    {
        Initialize(gameDirectory, panel);
        if (panel.Visible && !_attempted) _enabled = true;
        if (panel.RefreshDiscoveryRequested)
        {
            panel.RefreshDiscoveryRequested = false;
            Stop(panel);
            _enabled = true;
            _attempted = false;
        }
        if (!_enabled) return;
        try
        {
            if (_listener is null)
            {
                if (_attempted) return;
                _attempted = true;
                _listener = new(panel.DiscoveryPort);
                panel.DiscoveryStatus = $"Listening for LAN games on UDP {panel.DiscoveryPort}.";
                EngineLog.WriteLine($"Network discovery: {panel.DiscoveryStatus}");
            }
            if (!_listener.Update()) return;
            panel.Servers = _listener.Servers;
            panel.DiscoveryStatus = $"{panel.Servers.Count} LAN games; listening on UDP {_listener.Port}.";
            EngineLog.WriteLine($"Network discovery: {panel.DiscoveryStatus}");
            foreach (var server in panel.Servers)
                EngineLog.WriteLine($"Network discovery: {server.Name} at {server.Endpoint}; {server.Players}/{server.MaximumPlayers} players; version 0x{server.Version:X4}, flags 0x{server.Flags:X8}.");
        }
        catch (Exception error) when (error is SocketException or ArgumentException or IOException)
        {
            Stop(panel);
            panel.DiscoveryStatus = $"LAN discovery failed: {error.Message} Use Refresh LAN games to retry.";
            EngineLog.WriteLine(panel.DiscoveryStatus);
        }
    }

    public string SetOption(string value, string gameDirectory, GameServerDebugPanelState panel)
    {
        Initialize(gameDirectory, panel);
        switch (value.ToLowerInvariant())
        {
            case "status": return panel.DiscoveryStatus + string.Concat(panel.Servers.Select(
                server => $"\n{server.Name}: {server.Endpoint}, {server.Players}/{server.MaximumPlayers} players"));
            case "off":
                Stop(panel);
                panel.RefreshDiscoveryRequested = false;
                _enabled = false;
                _attempted = true;
                return panel.DiscoveryStatus = "LAN discovery stopped.";
            case "on" or "refresh": break;
            default:
                if (!int.TryParse(value, out var port) || port is < 1 or > ushort.MaxValue)
                    return "Use set server-discovery <on|off|refresh|status|UDP-port>.";
                panel.DiscoveryPort = port;
                break;
        }
        panel.RefreshDiscoveryRequested = true;
        return $"LAN discovery refresh requested on UDP {panel.DiscoveryPort}.";
    }

    private void Initialize(string gameDirectory, GameServerDebugPanelState panel)
    {
        if (_initialized) return;
        _initialized = true;
        var path = Path.Combine(gameDirectory, "Gameserver.cfg");
        try
        {
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadLines(path))
            {
                var separator = line.IndexOf(':');
                if (separator < 0 || !line[..separator].Trim().Equals("NETWORK_PORT_LISTEN", StringComparison.OrdinalIgnoreCase)) continue;
                if (int.TryParse(line[(separator + 1)..].Trim(), out var port) && port is >= 1 and <= ushort.MaxValue)
                    panel.DiscoveryPort = port;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            EngineLog.WriteLine($"Network discovery configuration: {error.Message}; using UDP {panel.DiscoveryPort}.");
        }
    }

    private void Stop(GameServerDebugPanelState panel)
    {
        Dispose();
        panel.Servers = [];
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _listener = null;
    }
}
