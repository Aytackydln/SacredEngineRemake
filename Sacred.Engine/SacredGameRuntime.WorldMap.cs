using System;
using Sacred.Engine.Scene;
using Sacred.World.Map;

namespace Sacred.Engine;

internal sealed partial class SacredGameRuntime
{
    private bool TrySetWorldMap(string value, out string message)
    {
        if (value.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            message = $"map {_worldMapControls.SelectedMap}; region names {_worldMapControls.RegionNamesVisible}; NPCs {_worldMapControls.RegionNpcsVisible}";
            return true;
        }
        if (_inGameScene is null || _scenes.ActiveSceneId == GameSceneId.GameLoading)
        {
            message = "Wait for the world to load before opening the map.";
            return true;
        }
        if (value.Equals("close", StringComparison.OrdinalIgnoreCase))
        {
            _scenes.RequestSwitch(GameSceneId.InGame);
            message = "map closed";
            return true;
        }
        if (!Enum.TryParse<WorldMapKind>(value, true, out var kind) || !Enum.IsDefined(kind))
        {
            message = "Usage: set map <ancaria|underworld|close|status>";
            return true;
        }
        _worldMapControls.SelectedMap = kind;
        _requestedMap = kind;
        _scenes.RequestSwitch(GameSceneId.WorldMap);
        message = $"opening {kind} map";
        return true;
    }

    private WorldMapKind? _requestedMap;

    private void ApplyWorldMapRequest()
    {
        if (_requestedMap is not { } kind || _scenes.ActiveSceneId != GameSceneId.WorldMap) return;
        // Activation selects the player's map; a console request explicitly overrides it.
        _worldMapControls.SelectedMap = kind;
        _requestedMap = null;
    }
}
