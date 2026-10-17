using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.World.Networking;

namespace Sacred.Engine.Multiplayer;

/// <summary>Console-driven owned walk with explicitly supplied timing. Native stat-derived speed/interactive input follow separately.</summary>
internal sealed class GameServerConsoleWalk
{
    private readonly GameServerClientController _client;
    private readonly SacredNetworkWorldBinding _world;
    private readonly Vector2[] _route;
    private readonly uint _control;
    private readonly float _speed;
    private readonly uint _word1F8;
    private readonly uint _flags;
    private GoldMovementBody _path;
    private Vector2 _position;
    private uint _facingBits;
    private int _waypoint;
    private float _publishElapsed;
    private Task? _send;
    private GoldMessage? _pending;
    private bool _ending;
    private bool _stopRequested;
    public bool Repeat { get; set; }
    public bool IsComplete { get; private set; }
    public string Status { get; private set; } = "starting owned console walk";

    public GameServerConsoleWalk(GameServerClientController client, SacredNetworkWorldBinding world,
        Vector2[] route, uint control174, float tilesPerSecond)
    {
        if(!world.IsCurrentSession || route.Length==0 || route.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y)) || !float.IsFinite(tilesPerSecond) || tilesPerSecond<=0)
            throw new ArgumentException("Walk requires a current world, finite route points and a positive supplied speed.");
        (_client,_world,_route,_control,_speed)=(client,world,route,control174,tilesPerSecond);
        var hero=world.LocalHero;
        var creature=hero.Creature ?? throw new InvalidOperationException("Local hero has no creature state.");
        _position=world.LocalPosition;
        _facingBits = hero.FacingBits ?? hero.BaseState.Word28;
        _word1F8=1;
        _flags=creature.Word200;
        _path=PrepareSegment();
        Publish(GoldMovementPacketCodec.CreateMovement(world.PlayerSlot,world.PlayerSlot,_control,Position(),_path));
        EngineLog.WriteLine($"Network walk started: owned hero {world.PlayerSlot}, {_position.X:0.###}/{_position.Y:0.###} -> first target {route[0].X:0.###}/{route[0].Y:0.###}, {route.Length} segments; supplied control {_control}, speed {_speed:0.###} tiles/sec.");
    }

    public void Update(float deltaSeconds)
    {
        if(IsComplete) return;
        try
        {
            if(!_world.IsCurrentSession || !_client.IsParticipantWorldApproved) throw new InvalidOperationException("Walking session ended.");
            if(_send is { IsCompleted:false }) return;
            if(_send is not null)
            {
                _send.GetAwaiter().GetResult();
                _client.WorldReplicas.Apply(GoldGameEventDecoder.Decode(_pending!));
                _send=null; _pending=null;
                if(_ending)
                {
                    if(!_stopRequested && (_waypoint<_route.Length || Repeat))
                    {
                        _ending=false;
                        if(_waypoint==_route.Length) _waypoint=0;
                        _publishElapsed=0;
                        _path=PrepareSegment();
                        Publish(GoldMovementPacketCodec.CreateMovement(_world.PlayerSlot,_world.PlayerSlot,_control,Position(),_path));
                        return;
                    }
                    IsComplete=true; Status="stopped";
                    EngineLog.WriteLine($"Network walk stopped: owned hero {_world.PlayerSlot}, native {Position().X}/{Position().Y}.");
                    return;
                }
            }
            if(_stopRequested)
            {
                _ending=true;
                Publish(GoldMovementPacketCodec.CreateEnd(_world.PlayerSlot,_world.PlayerSlot,Position(),_facingBits,0));
                return;
            }
            var remaining=Math.Max(0,deltaSeconds)*_speed;
            if(remaining>0 && _waypoint<_route.Length)
            {
                var direction=_route[_waypoint]-_position; var distance=direction.Length();
                if (distance > 0) _facingBits = SacredNetworkFacing.FromTileDirection(direction);
                if(distance<=remaining)
                {
                    _position=_route[_waypoint++];
                    _ending=true;
                    Publish(GoldMovementPacketCodec.CreateEnd(_world.PlayerSlot,_world.PlayerSlot,Position(),_facingBits,0));
                    return;
                }
                else { _position+=direction*(remaining/distance); remaining=0; }
            }
            if(_waypoint==_route.Length)
            {
                _ending=true;
                Publish(GoldMovementPacketCodec.CreateEnd(_world.PlayerSlot,_world.PlayerSlot,Position(),_facingBits,0));
                return;
            }
            _publishElapsed+=deltaSeconds;
            if(_publishElapsed<0.1f) return;
            _publishElapsed=0;
            var body=GoldMovementPacketCodec.CreatePath(_word1F8,_flags,0,new[]{Native(_position),Native(_route[_waypoint])},0);
            Publish(GoldMovementPacketCodec.CreateMovement(_world.PlayerSlot,_world.PlayerSlot,_control,Position(),body));
            Status=$"walking owned hero {_world.PlayerSlot}: {_position.X:0.###}/{_position.Y:0.###}";
        }
        catch(Exception error) { IsComplete=true; Status="failed: "+error.Message; EngineLog.WriteLine($"Network walk {Status}."); }
    }

    private void Publish(GoldMessage message) { _pending=message; _send=_client.SendOwnedMovementAsync(message); }
    public void Stop() { Repeat=false; _stopRequested=true; }
    private GoldMovementBody PrepareSegment()
    {
        var first=Native(_position); var target=Native(_route[_waypoint]);
        return GoldMovementPacketCodec.CreatePath(_word1F8,_flags,0,new[]{first,target},0);
    }
    private GoldObjectPosition Position()
    {
        if (!_world.TryGetPosition(_position, out var position))
            throw new InvalidOperationException("Console walking left loaded, confirmed terrain.");
        return position;
    }
    private static GoldMovementPoint Native(Vector2 position) => new((int)MathF.Round(position.X*SacredNetworkPosition.NativeUnitsPerTile),
        (int)MathF.Round(position.Y*SacredNetworkPosition.NativeUnitsPerTile));
}
