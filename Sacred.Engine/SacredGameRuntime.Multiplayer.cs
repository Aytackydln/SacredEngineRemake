using System;
using Sacred.Engine.Multiplayer;

namespace Sacred.Engine;

internal sealed partial class SacredGameRuntime
{
    private readonly GameServerClientController _gameServer = new();
    private readonly GameServerCompatibilityCheck _gameServerFiles = new();

    internal void RequestServerConnection(string endpoint) => _cheats.Submit("set server " + endpoint);

    private bool TrySetServerCompatibilityOption(string option, string value, out string message)
    {
        message = string.Empty;
        if (!option.Equals("server-files", StringComparison.OrdinalIgnoreCase)) return false;
        message = value.ToLowerInvariant() switch
        {
            "check" => _gameServerFiles.Start(_gameDirectory),
            "status" => _gameServerFiles.Status,
            _ => "use set server-files <check|status>"
        };
        return true;
    }
}
