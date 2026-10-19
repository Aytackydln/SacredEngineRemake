using System.Collections.ObjectModel;
using System.Numerics;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.World;

/// <summary>Gameplay-owned projectile identities, release transitions, targeting and flight.
/// Animation adapters supply the sampled hand position; renderers consume the resulting instances.</summary>
public sealed class GameplayBowProjectiles
{
    private readonly GoldWorldReplica _world;
    private readonly Action<string> _log;
    private readonly Dictionary<uint, GameplayBowProjectile> _entries = [];
    public GameplayBowProjectiles(GoldWorldReplica world, Action<string> log)
    {
        (_world, _log) = (world, log);
        Entries = new ReadOnlyDictionary<uint, GameplayBowProjectile>(_entries);
    }
    public IReadOnlyDictionary<uint, GameplayBowProjectile> Entries { get; }

    public void Update(float seconds, Func<GoldBowProjectile, GameplayBowReleasePose?> sampleRelease,
        Func<uint, Vector3?> targetCenter, Func<Vector2, float> sceneGroundHeight)
    {
        var requests = _world.BowProjectiles.Entries;
        foreach (var reference in _entries.Keys.Where(id => !requests.ContainsKey(id)).ToArray()) _entries.Remove(reference);
        foreach (var (reference, request) in requests)
        {
            if (!_entries.TryGetValue(reference, out var arrow) || arrow.Request.Generation != request.Generation)
            {
                _entries[reference] = arrow = new(request);
                // Original BeeEffGee is a temporary equipped weapon. Compact equipment
                // packets also reach peers without a complete updated creature flag bank.
                if ((request.CreatureFlags & 0x200000) != 0 || request.EquipmentReferences.Any(id =>
                        _world.Objects.TryGetValue(id, out var item) && item.BaseState.ObjectType == GameplayBfgFlight.WeaponResource))
                    arrow.Type = GameplayBfgFlight.Resource;
            }
            if (arrow.Retired) continue;
            if (arrow.IsBfg && request.Index != 0) { arrow.Retired = true; continue; }
            if (arrow.IsBfg && (request.FxNotification & 2) != 0) { arrow.Retired = true; continue; }
            if (arrow.Flight is null)
            {
                if (!_world.Objects.TryGetValue(request.Attack.ObjectReference, out var actor) ||
                    actor.InstanceVersion != request.AttackerInstance || actor.AttackVersion != request.AttackVersion ||
                    !ReferenceEquals(actor.Attack, request.Attack) || request.Action.Released)
                { arrow.Retired = true; continue; }
                if (request.Action.CombatArt != 0 || !arrow.IsBfg && (request.CreatureFlags & 0x400000) != 0)
                {
                    arrow.Retired = true;
                    _log($"Network bow projectile pending combat-art factory: ref {reference}; art {request.Action.CombatArt}; creature flags 0x{actor.Creature?.Word200:X8}.");
                    continue;
                }
                if (sampleRelease(request) is not { } release) continue;
                if (release.Age >= (arrow.IsBfg ? GameplayBfgFlight.LifetimeSeconds : GameplayArrowFlight.LifetimeSeconds))
                { arrow.Retired = true; continue; }
                Vector3 direction;
                if (arrow.IsBfg && _world.Objects.TryGetValue(request.Action.TargetReference, out var targetActor))
                {
                    // EiBow aims BFG in the ground plane from the actor anchor; muzzle height
                    // does not tilt the energy bolt toward a neck bone.
                    var dx = targetActor.BaseState.X - actor.BaseState.X;
                    var dy = targetActor.BaseState.Y - actor.BaseState.Y;
                    direction = new((dx - dy) * .707f, (dx + dy) * .707f, 0);
                }
                else if (!arrow.IsBfg && targetCenter(request.Action.TargetReference) is { } target) direction = target - release.Source;
                else
                {
                    var facing = actor.Physical?.Vector70 ?? GoldPhysicalFacing.ToDirection(request.Attack.FacingBits);
                    direction = new((facing.X - facing.Y) * .707f, (facing.X + facing.Y) * .707f, 0);
                }
                if (!arrow.IsBfg) direction = Vector3.TransformNormal(direction,
                    Matrix4x4.CreateRotationZ(GameplayBowRelease.Spread(0, request.Index, request.Count)));
                arrow.Flight = arrow.IsBfg ? new GameplayBfgFlight(release.Source, direction) : new GameplayArrowFlight(release.Source, direction);
                arrow.Flight.Advance(release.Age, (from, to) => AboveTerrain(from, to, sceneGroundHeight));
                _log($"Network {(arrow.IsBfg ? "BFG bolt" : "arrow")} released: ref {reference}; attacker {request.Attack.ObjectReference}; resource {arrow.Type}; source {release.Source}; direction {arrow.Flight.Direction}; age {release.Age:0.###}s.");
            }
            else arrow.Flight.Advance(seconds, (from, to) => AboveTerrain(from, to, sceneGroundHeight));
            if (!arrow.Flight.Active) arrow.Retired = true;
        }
        foreach (var projectile in _entries.Values)
            if (projectile.Retired) _world.BowProjectiles.Retire(projectile.Request.Reference, projectile.Request.Generation);
    }
    private static bool AboveTerrain(Vector3 from, Vector3 to, Func<Vector2, float> groundHeight)
    {
        // Probe the whole step so a slow frame cannot skip an intervening terrain rise.
        var count = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(from, to) / 10));
        for (var i = 1; i <= count; i++)
        {
            var point = Vector3.Lerp(from, to, (float)i / count);
            if (GameplayProjectileCoordinates.ToScene(point).Z <= groundHeight(GameplayProjectileCoordinates.ToTiles(point))) return false;
        }
        return true;
    }
    public void Clear() => _entries.Clear();
}
