using System.Numerics;

namespace Sacred.GameplayHost.World;

/// <summary>Presentation supplies reach and original motion availability; gameplay decides the effect.</summary>
public readonly record struct GameplayInteractionContext(Vector2 ActorPosition, byte Surface,
    bool InReach, bool CanActivate, bool CanReset);
