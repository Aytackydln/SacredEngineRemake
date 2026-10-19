using System.Numerics;
using Sacred.Network.Gameplay;
using Sacred.World.Geometry;
using Sacred.World.Networking;

namespace Sacred.GameplayHost.World;

/// <summary>Gold SetupFly/ContinueFly presentation. Authoritative anchors stay in the replica.</summary>
public sealed class GameplayDroppedItemPlayback
{
    // Gold 0x44B2C3 and 0x44EA65: XY projection rate, drag and vertical acceleration.
    private const float HorizontalRate = .1f;
    private const float Drag = -.1f;
    private static readonly float Gravity = BitConverter.UInt32BitsToSingle(0xc39167ae);
    private GoldObjectFlightEvent? _flight;
    private long _instance = -1, _version = -1;
    private float _elapsed;
    private Vector2 _start;
    private Vector2 _nativeStart;
    private Vector3 _velocity, _angles;
    public GameplayDroppedItemPose Pose { get; private set; }

    public void Synchronize(GoldReplicatedObject obj, Func<Vector2, float> groundHeight)
    {
        if (_instance == obj.InstanceVersion && _version == obj.FlightVersion) return;
        var replacement = _instance != obj.InstanceVersion;
        _instance = obj.InstanceVersion; _version = obj.FlightVersion;
        _flight = obj.Flight; _elapsed = 0;
        _start = SacredNetworkPosition.ToTiles(obj.BaseState);
        _nativeStart = new(obj.BaseState.X, obj.BaseState.Y);
        var exactPivot = (obj.BaseState.Flags & GoldPhysicalStateLayout.VectorCCFlag) != 0 && obj.Physical is not null;
        if (exactPivot)
        {
            var physical = obj.Physical!;
            _start += new Vector2(physical.SelectedVector.X - physical.BaseState.X, physical.SelectedVector.Y - physical.BaseState.Y)
                / SacredNetworkPosition.NativeUnitsPerTile;
        }
        var ground = groundHeight(_start);
        if (replacement || _flight is null)
        {
            var direction = obj.Physical?.Vector70 ?? default;
            _angles = new(direction.X, direction.Y, direction.Z);
            Pose = new(_start, exactPivot ? obj.Physical!.SelectedVector.Z : ground, ground,
                GameplayDroppedItemOrientation.SettledMatrix(direction),
                (obj.BaseState.Flags & GoldObjectFlightLayout.FlyingFlag) != 0 && _flight is null);
        }
        if (_flight is not { } flight) return;
        _velocity = new(flight.Translation.X, flight.Translation.Y, flight.Translation.Z);
        // dropItem resets the model to the supplied height; it does not use Weapon.pak's inventory camera.
        Pose = Pose with { Position = _start, Height = flight.InitialHeight, GroundHeight = ground, IsFlying = true };
    }

    public void Advance(float seconds, Func<Vector2, float> groundHeight)
    {
        if (_flight is not { } flight || !Pose.IsFlying || !float.IsFinite(seconds) || seconds <= 0) return;
        if (flight.IsItemArc) AdvanceArc(seconds, flight, groundHeight);
        else AdvanceFree(seconds, flight, groundHeight);
    }

    private void AdvanceArc(float seconds, GoldObjectFlightEvent flight, Func<Vector2, float> groundHeight)
    {
        _elapsed += seconds;
        var target = new Vector2((int)flight.Translation.X, (int)flight.Translation.Y) / SacredNetworkPosition.NativeUnitsPerTile;
        if (_elapsed >= flight.Rotation.Z || _elapsed < 0)
        {
            var ground = groundHeight(target);
            Pose = Pose with { Position = target, Height = ground, GroundHeight = ground, IsFlying = false };
            return;
        }
        var progress = _elapsed / flight.Rotation.Z;
        // Native tests the whole arc in six probes per render, leaving the last successful probe as its pose.
        for (var sample = progress * (1f / 15f); sample < progress * 1.1f; sample += progress * .2f)
        {
            var native = _nativeStart + (new Vector2(flight.Translation.X, flight.Translation.Y) - _nativeStart) * sample;
            var position = new Vector2((int)native.X, (int)native.Y) / SacredNetworkPosition.NativeUnitsPerTile;
            var ground = groundHeight(position);
            var height = flight.Rotation.Y * (1 - 4 * (sample - .5f) * (sample - .5f));
            Pose = Pose with { Position = position, Height = ground + height, GroundHeight = ground };
        }
    }

    private void AdvanceFree(float seconds, GoldObjectFlightEvent flight, Func<Vector2, float> groundHeight)
    {
        // Native MyPosition XY is in its model camera. Undo that camera before returning terrain tiles.
        var projected = new Vector2(_velocity.X, _velocity.Y) * (seconds * HorizontalRate);
        var iso = new Vector2(projected.X * (1024f / 534f), projected.Y * WorldModelPose.Scale / MathF.Sqrt(5));
        var position = Pose.Position + IsometricProjection.IsoToWorld(iso);
        var height = Pose.Height + _velocity.Z * seconds;
        var ground = groundHeight(position);
        // ContinueFly constructs orientation from the previous angles, then advances them.
        var orientation = GameplayDroppedItemOrientation.ModelMatrix(_angles);
        _angles += new Vector3(flight.Rotation.X, flight.Rotation.Y, flight.Rotation.Z) * seconds;
        _velocity += new Vector3(_velocity.X * Drag, _velocity.Y * Drag, Gravity) * seconds;
        if (height < ground) { height = ground; _velocity = Vector3.Zero; }
        var flying = MathF.Abs(_velocity.X) >= 1 || MathF.Abs(_velocity.Y) >= 1 ||
            MathF.Abs(_velocity.Z) >= 1 || MathF.Abs(height - ground) >= 1;
        Pose = new(position, height, ground, orientation, flying);
    }
}
