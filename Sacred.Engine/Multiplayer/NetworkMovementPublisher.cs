using System;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using Sacred.World.Networking;

namespace Sacred.Engine.Multiplayer;

/// <summary>Publishes owned routes and stops, preserving the peer's animation clock on route refreshes.</summary>
internal sealed class NetworkMovementPublisher(GameServerClientController client, SacredNetworkWorldBinding world)
{
    private Task? _send;
    private bool _started;
    private uint _lastAction;
    private float _elapsed;
    private uint _lastFacing = world.LocalHero.FacingBits ?? world.LocalHero.BaseState.Word28;
    public string? Error { get; private set; }
    public int SentCount { get; private set; }

    public void Update(Vector2 position, Vector2 target, Vector2 facing, bool moving, bool walking, float deltaSeconds)
    {
        if (Error is not null || !world.IsCurrentSession || !client.IsParticipantWorldApproved) return;
        try
        {
            _elapsed += deltaSeconds;
            if (_send is { IsCompleted: false }) return;
            _send?.GetAwaiter().GetResult();
            _send = null;
            var actor = world.LocalHero;
            if (!world.TryGetPosition(position, out var location))
                throw new InvalidOperationException("Movement left loaded, confirmed terrain.");
            var current = new GoldMovementPoint(location.X, location.Y);
            var facingBits = facing == Vector2.Zero ? actor.FacingBits ?? actor.BaseState.Word28 : SacredNetworkFacing.FromTileDirection(facing);
            GoldMessage? message = null;
            if (moving)
            {
                var action = walking ? 1u : 2u;
                var creature = actor.Creature!;
                var end = Native(target);
                if (!_started || action != _lastAction)
                {
                    message = GoldMovementPacketCodec.CreateMovement(world.PlayerSlot, world.PlayerSlot, client.LocalMovementControl,
                        location, GoldMovementPacketCodec.CreatePath(action, creature.Word200, 0, new[] { current, end }, 0));
                    EngineLog.WriteLine($"Network input: {(walking ? "walk" : "run")} started for owned ref {world.PlayerSlot}, target {target.X:0.###}/{target.Y:0.###}.");
                }
                else if (_elapsed >= 0.1f)
                {
                    message = GoldMovementPacketCodec.CreateMovement(world.PlayerSlot, world.PlayerSlot, client.LocalMovementControl,
                        location, GoldMovementPacketCodec.CreatePath(action, creature.Word200, 0, new[] { current, end }, 0));
                }
                _lastAction = action;
            }
            else if (_started || facingBits != _lastFacing)
            {
                message = GoldMovementPacketCodec.CreateEnd(world.PlayerSlot, world.PlayerSlot, location, facingBits, 0);
                EngineLog.WriteLine($"Network input: owned ref {world.PlayerSlot} stopped at {position.X:0.###}/{position.Y:0.###}.");
            }
            if (message is null) return;
            _send = client.SendOwnedMovementAsync(message);
            world.Replicas.Apply(GoldGameEventDecoder.Decode(message));
            _started = moving;
            _lastFacing = facingBits;
            _elapsed = 0;
            SentCount++;
        }
        catch (Exception error) { Error = error.Message; EngineLog.WriteLine("Network movement failed: " + Error); }
    }

    private static GoldMovementPoint Native(Vector2 point) => new((int)MathF.Round(point.X * SacredNetworkPosition.NativeUnitsPerTile),
        (int)MathF.Round(point.Y * SacredNetworkPosition.NativeUnitsPerTile));
}
