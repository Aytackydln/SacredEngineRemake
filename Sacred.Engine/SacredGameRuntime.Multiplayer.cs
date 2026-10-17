using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Sacred.Core.Pax.Hero;
using Sacred.Core.World;
using Sacred.Engine.Multiplayer;
using Sacred.Engine.Scene;
using Sacred.Network.Admission;

namespace Sacred.Engine;

internal sealed partial class SacredGameRuntime
{
    private readonly GameServerClientController _gameServer = new();
    private readonly GameServerCompatibilityCheck _gameServerFiles = new();
    private GameServerJoinSession? _serverJoin;
    private GameServerConsoleWalk? _serverWalk;

    private bool TrySetServerWalkOption(string option,string value,out string message)
    {
        message=string.Empty;
        if(!option.Equals("server-walk",StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            if(value.Equals("status",StringComparison.OrdinalIgnoreCase)) { message=_serverWalk?.Status ?? "no owned walk requested"; return true; }
            if(value.Equals("stop",StringComparison.OrdinalIgnoreCase)) { _serverWalk?.Stop(); message="owned walking stop requested"; return true; }
            if(_serverWalk is { IsComplete:false }) throw new InvalidOperationException("Wait for the owned walk to finish before another target.");
            var fields=value.Split(' ',StringSplitOptions.RemoveEmptyEntries);
            if(fields.Length==0) throw new ArgumentException("Supply a walking target or status/stop.");
            var patrol=fields[0].Equals("patrol",StringComparison.OrdinalIgnoreCase);
            if(patrol) fields=fields[1..];
            if(fields.Length!=3) throw new ArgumentException("Use set server-walk [patrol] <tile-x,tile-y> <native control174> <tiles/sec>.");
            var coordinates=fields[0].Split(',');
            if(fields.Length!=3 || coordinates.Length!=2 || !float.TryParse(coordinates[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var x) ||
               !float.TryParse(coordinates[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var y) ||
               !uint.TryParse(fields[1],out var control) || !float.TryParse(fields[2],NumberStyles.Float,CultureInfo.InvariantCulture,out var speed) || !float.IsFinite(x) || !float.IsFinite(y))
                throw new ArgumentException("Use set server-walk [patrol] <tile-x,tile-y> <native control174> <tiles/sec>, status, or stop.");
            var route=RuntimeScene.FindNetworkWalkRoute(new(x,y));
            if(patrol) route=route.Concat(route.Reverse().Skip(1).Append(RuntimeScene.NetworkWorld.LocalPosition)).ToArray();
            _serverWalk=new(_gameServer,RuntimeScene.NetworkWorld,route,control,speed) { Repeat=patrol };
            message=_serverWalk.Status;
        }
        catch(Exception error) { message="network walk failed: "+error.Message; }
        return true;
    }

    private bool TrySetServerJoinOption(string option,string value,out string message)
    {
        message=string.Empty;
        if (!option.Equals("server-join",StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Equals("status",StringComparison.OrdinalIgnoreCase)) { message=_serverJoin?.Status ?? "no join requested"; return true; }
        var fields=value.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        try
        {
            if (_serverJoin is { IsFinished: false }) throw new InvalidOperationException("A join is already in progress. Disconnect before retrying.");
            if(fields.Length!=2 || !int.TryParse(fields[1],out var selection)) throw new ArgumentException("Use set server-join <host:port> <saved selection 1..8>.");
            if (!Uri.TryCreate("tcp://" + fields[0], UriKind.Absolute, out var endpoint) || endpoint.Port is < 1 or > 65535 || endpoint.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
                throw new ArgumentException("Supply a TCP endpoint in host:port form.");
            _serverJoin=new(_gameServer,_gameDirectory,fields[0],selection) { SectorScriptSelection = _campaignSelection };
            _debugUiControls.GameServer.Endpoint = fields[0];
            _debugUiControls.GameServer.Selection = selection;
            _debugUiControls.GameServer.Visible = true;
            message=_serverJoin.Status;
        }
        catch(Exception error) { message=error.Message; }
        return true;
    }

    private void UpdateServerJoin() => _serverJoin?.Update(replicas =>
    {
        if(_inGameScene is null) return false;
        if (_scenes.ActiveSceneId != GameSceneId.InGame)
        {
            _scenes.RequestSwitch(GameSceneId.InGame);
            return false;
        }
        _inGameScene.ConfigureNetworkConnection(_gameServer);
        _inGameScene.BindNetworkWorld(replicas);
        return true;
    }, () =>
    {
        if (_inGameScene?.NetworkBindingError is { } error) throw new InvalidOperationException(error);
        if (_inGameScene?.NetworkWorldReady == true) _serverJoin!.SetSectorPatchWorld(_inGameScene.NetworkWorld);
        return _inGameScene?.NetworkWorldReady == true;
    });

    internal void RequestServerConnection(string endpoint) => _cheats.Submit("set server " + endpoint);
    internal void RequestServerJoin(string endpoint, int selection) => _cheats.Submit($"set server-join {endpoint} {selection}");

    private bool TrySetServerDisconnectOption(string option, string value, out string message)
    {
        message = string.Empty;
        if (!option.Equals("server", StringComparison.OrdinalIgnoreCase) || !value.Equals("disconnect", StringComparison.OrdinalIgnoreCase)) return false;
        _serverJoin?.Cancel();
        _serverWalk?.Stop();
        return _gameServer.TrySetOption(option, value, out message);
    }

    private bool TrySetServerCharacterOption(string option, string value, out string message)
    {
        message = string.Empty;
        if (!option.Equals("server-character", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            if (value.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                _gameServer.SetFirstContact(null);
                message = "saved-character admission cleared";
                return true;
            }
            var fields = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if(fields.Length==2 && fields[0].Equals("sectors",StringComparison.OrdinalIgnoreCase) && ushort.TryParse(fields[1],out var sector))
            {
                message=_gameServer.RequestSectorInterest(sector);
                return true;
            }
            if (fields.Length == 1 && fields[0].Equals("world",StringComparison.OrdinalIgnoreCase))
            {
                if (!_gameServer.IsParticipantWorldApproved) throw new InvalidOperationException("Network world binding requires native participant approval.");
                RuntimeScene.ConfigureNetworkConnection(_gameServer);
                message = RuntimeScene.BindNetworkWorld(_gameServer.WorldReplicas);
                _scenes.RequestSwitch(GameSceneId.InGame);
                return true;
            }
            if (fields.Length == 1 && fields[0].Equals("world-status",StringComparison.OrdinalIgnoreCase))
            {
                message = _inGameScene?.NetworkWorldStatus ?? "in-game scene is not loaded";
                return true;
            }
            if (fields.Length == 1 && fields[0].Equals("sectors", StringComparison.OrdinalIgnoreCase))
            {
                var index = SacredSectorIndex.Read(Path.Combine(_gameDirectory, "World", "sectors.keyx"));
                message = _gameServer.RequestInitialSectors(index);
                return true;
            }
            if (fields.Length == 2 && fields[0].Equals("inspect", StringComparison.OrdinalIgnoreCase) && int.TryParse(fields[1], out var inspectSelection))
            {
                message = GameServerSavedCharacterInspection.Inspect(_gameDirectory, inspectSelection);
                return true;
            }
            if (fields.Length == 2 && fields[0].Equals("plan", StringComparison.OrdinalIgnoreCase) && int.TryParse(fields[1], out var planSelection))
            {
                var source = GameServerSavedCharacterReader.Read(_gameDirectory, planSelection);
                message = _gameServer.StageCharacterReferences(planSelection, source);
                return true;
            }
            if (fields.Length != 5 || !int.TryParse(fields[0], out var selection) || selection is < 1 or > 8 ||
                !byte.TryParse(fields[1], out var byte7) || !byte.TryParse(fields[2], out var speed) ||
                !byte.TryParse(fields[3], out var flag9) || !int.TryParse(fields[4], out var lobbyIdentity))
            {
                message = "use set server-character <selection 1..8> <profile byte7> <speed> <profile flag9> <lobby identity>, inspect <selection>, plan <selection>, sectors [authored sector ID], world, world-status, or clear";
                return true;
            }
            if (_gameServerFiles.Compatibility is not { } compatibility)
            {
                message = "run set server-files check and wait for completion before selecting an admission character";
                return true;
            }
            var hero = SacredHeroPaxProfile.ReadSelected(_gameDirectory, selection);
            var parameters = new GoldFirstContactParameters(hero.Name, (byte)selection, byte7, speed, flag9, lobbyIdentity);
            _gameServer.SetFirstContact(GoldFirstContact.Create(hero.Profile, parameters, compatibility));
            message = $"saved hero selection {selection} loaded: level {hero.Level}, 556 profile bytes; connect with set server <host:port>; hero/world import pending";
            EngineLog.WriteLine($"Network loading complete: {message}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            message = $"saved-character admission failed: {error.Message}";
            EngineLog.WriteLine($"Network: {message}.");
        }
        return true;
    }

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
