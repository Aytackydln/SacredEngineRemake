using System.Numerics;
using Sacred.Network.Gameplay;

namespace Sacred.World.Networking;

/// <summary>Remote presentation of received routes and corrections. Replicas keep
/// their reported anchors; NPCs and players share the same native path clock.</summary>
public sealed class SacredNetworkMovementPlayback
{
    private GoldMovementEvent? _movement;
    private GoldMovementPathPlayback _path = new();
    private GoldObjectBaseState? _basis;
    private long _instanceVersion;
    private long _positionVersion;
    private long _movementVersion;
    private Vector2 _position, _correction, _anchorTarget;
    private float _correctionSeconds;
    private bool _hasPath;
    public bool Moving { get; private set; }
    public bool Snapped { get; private set; }
    /// <summary>Received native path speed, before presentation corrections.</summary>
    public int Speed => _path.Speed;
    public Vector2 Direction => _hasPath ? new(_path.Direction.X, _path.Direction.Y) : Vector2.Zero;

    public Vector2 Update(GoldReplicatedObject actor, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var creature = actor.Creature ?? throw new ArgumentException("Creature movement requires a creature state.", nameof(actor));
        var reported = SacredNetworkPosition.ToTiles(actor.BaseState);
        var changedAnchor = _basis is null || actor.PositionVersion != _positionVersion;
        var replaced = changedAnchor && actor.PositionOpcode is 0x169 or 0x16B or 0x16C or 0x16E or 0x16F;
        var relocation = changedAnchor && actor.PositionOpcode is 0x165 or GoldCombatLayout.BowOpcode or GoldCombatLayout.BattleOpcode;
        var correction = changedAnchor && actor.PositionOpcode == GoldCreaturePositionCorrectionCodec.Opcode;
        Snapped = _basis is null || actor.InstanceVersion != _instanceVersion || replaced || relocation || correction;
        var newMovement = actor.MovementVersion != _movementVersion || !ReferenceEquals(actor.Movement, _movement);
        if (Snapped && (!correction || _basis is null || actor.InstanceVersion != _instanceVersion))
        {
            _path = new();
            _path.ReadBank(creature.Block2ED, new(actor.BaseState.X, actor.BaseState.Y));
            // The serialized bank predates incremental stops/cancellations. Asset
            // loading or equipment rebuilding must not resurrect that old route.
            _hasPath = !relocation && actor.MovementVersion == actor.SnapshotMovementVersion &&
                actor.Creature.Word1F8 is 1 or 2 && _path.Moving;
            _position = reported;
            _anchorTarget = reported;
            _correction = Vector2.Zero;
        }
        else if (correction)
        {
            _path.ApplyCorrection(new(actor.BaseState.X, actor.BaseState.Y));
            _position = reported; _anchorTarget = reported;
            _correction = Vector2.Zero; _correctionSeconds = 0;
        }
        if (newMovement && actor.Movement?.Body is { } body)
        {
            _anchorTarget = reported;
            var anchor = new GoldMovementPoint(actor.BaseState.X, actor.BaseState.Y);
            if (body.Opcode == GoldMovementLayout.ActionOpcode) _path.ReadBank(body.ActionBlock, anchor);
            else _path.ReadPath(body.Path, body.PathIndex!.Value, anchor);
            _hasPath = body.Word1F8 is 1 or 2 && _path.Moving;
            _correction = Snapped ? Vector2.Zero : _position - reported;
            _correctionSeconds = .1f;
        }
        else if (!Snapped && newMovement && actor.Movement is null)
        {
            _hasPath = false;
            // 0x164 clears movement without a location. Keep the locally advanced
            // position; the last wire anchor can be seconds behind the actor.
            _anchorTarget = changedAnchor ? reported : _position;
            _correction = _position - _anchorTarget;
            _correctionSeconds = .1f;
        }
        if (creature.IsSleeping)
        {
            _hasPath = false;
            _correction = Vector2.Zero; _correctionSeconds = 0;
            _anchorTarget = _position;
        }
        var previous = _position;
        if (_hasPath)
        {
            var point = _path.Advance(seconds);
            var target = new Vector2(point.X, point.Y) / SacredNetworkPosition.NativeUnitsPerTile;
            _position = target + Correction(seconds);
            Moving = _path.Moving;
        }
        else if (newMovement || _correctionSeconds > 0)
        {
            _position = _anchorTarget + Correction(seconds);
            Moving = actor.Movement is not null || Vector2.DistanceSquared(previous, _position) > .0000001f;
        }
        else
        {
            _position = _anchorTarget;
            Moving = false;
        }
        _movement = actor.Movement;
        _movementVersion = actor.MovementVersion;
        _basis = actor.BaseState;
        _instanceVersion = actor.InstanceVersion;
        _positionVersion = actor.PositionVersion;
        return _position;
    }

    private Vector2 Correction(float seconds)
    {
        _correctionSeconds = Math.Max(0, _correctionSeconds - seconds);
        return _correction * (_correctionSeconds / .1f);
    }
}
