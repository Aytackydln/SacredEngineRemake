using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.World;

public sealed class GameplayBowProjectile(GoldBowProjectile request)
{
    public GoldBowProjectile Request { get; } = request;
    public uint Type { get; internal set; } = GameplayBowRelease.ProjectileType(request.Action.CombatArt);
    public bool IsBfg => Type == GameplayBfgFlight.Resource;
    public IGameplayProjectileFlight? Flight { get; internal set; }
    public bool Retired { get; internal set; }
}
