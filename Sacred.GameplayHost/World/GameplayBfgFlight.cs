using System.Numerics;

namespace Sacred.GameplayHost.World;

/// <summary>Gold cObjectFX 0x5A6680 uses particle +0x98 (BFG initializer: 200).
/// cParticleSystem_BFG 0x77F1C0 ends playback at three seconds.</summary>
public sealed class GameplayBfgFlight : IGameplayProjectileFlight
{
    public const uint Resource = 875;
    public const uint WeaponResource = 3809;
    public const float NativeSpeed = 200;
    public const float LifetimeSeconds = 3;
    public GameplayBfgFlight(Vector3 source, Vector3 direction)
    {
        if (!Finite(source) || !Finite(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        Position = source;
        direction.Z = 0;
        Direction = direction.Length() >= 1e-7f ? Vector3.Normalize(direction) : Vector3.Zero;
    }
    public Vector3 Position { get; private set; }
    public Vector3 Direction { get; }
    public float Age { get; private set; }
    public bool Active { get; private set; } = true;
    public void Advance(float seconds, Func<Vector3, Vector3, bool>? canTravel = null)
    {
        if (!Active || !float.IsFinite(seconds) || seconds <= 0) return;
        Position += Direction * (NativeSpeed * seconds);
        Age += seconds;
        if (Age >= LifetimeSeconds) Active = false;
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
