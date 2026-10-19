using System.Numerics;

namespace Sacred.GameplayHost.World;

/// <summary>Ordinary Gold cArrow3D event 9 / ContinueFly (0x5BD2D0 / 0x5BB010).
/// Coordinates are in the original model camera, before renderer projection.</summary>
public sealed class GameplayArrowFlight : IGameplayProjectileFlight
{
    public const float NativeSpeed = 600;
    public const float LifetimeSeconds = 1.5f;
    public GameplayArrowFlight(Vector3 source, Vector3 direction)
    {
        if (!Finite(source) || !Finite(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        Position = source;
        Direction = direction.Length() >= 1e-7f ? Vector3.Normalize(direction) : Vector3.Zero;
    }
    public Vector3 Position { get; private set; }
    public Vector3 Direction { get; }
    public float Age { get; private set; }
    public bool Active { get; private set; } = true;
    public void Advance(float seconds, Func<Vector3, Vector3, bool>? canTravel = null)
    {
        if (!Active || !float.IsFinite(seconds) || seconds <= 0) return;
        // Gold checks the remaining lifetime at entry, then moves the whole frame.
        if (Age >= LifetimeSeconds) { Active = false; return; }
        var next = Position + Direction * (NativeSpeed * seconds);
        Age += seconds;
        if (canTravel?.Invoke(Position, next) == false) { Active = false; return; }
        Position = next;
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
