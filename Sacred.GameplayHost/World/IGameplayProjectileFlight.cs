using System.Numerics;

namespace Sacred.GameplayHost.World;

public interface IGameplayProjectileFlight
{
    Vector3 Position { get; }
    Vector3 Direction { get; }
    float Age { get; }
    bool Active { get; }
    void Advance(float seconds, Func<Vector3, Vector3, bool>? canTravel = null);
}
