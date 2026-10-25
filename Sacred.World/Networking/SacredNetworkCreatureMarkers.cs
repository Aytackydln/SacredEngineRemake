using System.Numerics;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Runtime;
using Sacred.Network.Gameplay;
using Sacred.World.Particles;

namespace Sacred.World.Networking;

public sealed record WorldCreatureMarkerActor(GoldReplicatedObject Replica, Vector2 Position, float GroundHeight,
    uint BlockRadius, bool Focused = false);

/// <summary>Enemy hover and local-player combat health rings; authoritative health updates change their arcs.</summary>
public sealed class SacredNetworkCreatureMarkers
{
    private readonly Dictionary<uint, (long Instance, float Remaining)> _visible = [];
    private long _session = -1;
    public IReadOnlyList<WorldCreatureMarker> Markers { get; private set; } = [];

    public void Update(GoldWorldReplica world, uint localReference, IEnumerable<WorldCreatureMarkerActor> actors,
        SacredCreatureMarkerBalance tuning, SacredCreatureBalance levels, uint difficulty, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (_session != world.Consumers.Session) { _visible.Clear(); _session = world.Consumers.Session; }
        foreach (var (reference, timer) in _visible.ToArray())
        {
            var remaining = SacredCreatureHealthDisplay.Advance(timer.Remaining, seconds);
            if (remaining <= 0) _visible.Remove(reference);
            else _visible[reference] = (timer.Instance, remaining);
        }
        var result = new List<WorldCreatureMarker>();
        var hero = world.Objects.GetValueOrDefault(localReference);
        var heroLevel = hero?.Values?.Level ?? 1;
        foreach (var actor in actors)
        {
            var replica = actor.Replica;
            if (replica.Creature is not { } creature || replica.Values is not { MaximumHitPoints: > 0 } values ||
                (replica.CurrentHitPoints ?? values.CurrentHitPoints) <= 0 ||
                replica.IsDead ||
                (replica.BaseState.Flags & (GoldObjectLifecycleLayout.InactiveFlag | GoldObjectStateLayout.HiddenFlag | 0x100000)) != 0) continue;
            var reference = replica.BaseState.ObjectReference;
            var local = reference == localReference;
            var enemy = !local && hero?.Creature is not null && SacredNetworkCreatureRelations.AreEnemies(world, replica, hero);
            // Gold 61258A raises the hovered enemy timer to one second. 599A86
            // refreshes only the local player's timer from its fight-mode bit.
            if (local ? creature.IsInFightMode : enemy && actor.Focused) Show(reference);
            var remaining = (local || enemy) && _visible.TryGetValue(reference, out var timer) && timer.Instance == replica.InstanceVersion
                ? timer.Remaining : 0;
            var champion = ((replica.Physical?.WordB0 ?? 0) & GoldPhysicalStateLayout.ChampionGroundSymbolFlag) != 0;
            var elite = (creature.BehaviorFlags & GoldCreatureStateLayout.EliteGroundSymbolFlag) != 0;
            if (remaining <= 0 && !champion && !elite) continue;
            var alpha = SacredCreatureHealthDisplay.Alpha(remaining);
            var normalizedLevel = SacredCreatureProgressionRules.BaseLevelForDifficulty(values.Level, difficulty, levels);
            var color = tuning.LevelColor((int)normalizedLevel, heroLevel) | alpha << 24;
            var border = (color & 0xFFFF0000) | 0xFF0000;
            var radius = (actor.BlockRadius == 0 ? 50 : actor.BlockRadius) >> 1;
            if ((creature.Word200 & 0x40) != 0)
            {
                radius = radius * 3 / 2;
                if (replica.BaseState.ObjectType is > 0 and <= 9)
                {
                    color = Fade(tuning.PlayerHealthColor, alpha);
                    border = Fade(tuning.PlayerBorderColor, alpha);
                }
            }
            result.Add(new(reference, actor.Position, actor.GroundHeight, radius,
                Math.Clamp((replica.CurrentHitPoints ?? values.CurrentHitPoints) / (float)values.MaximumHitPoints, 0, 1),
                color, border, remaining > 0, champion, elite));
        }
        Markers = result;

        void Show(uint reference)
        {
            if (world.Objects.TryGetValue(reference, out var actor) && actor.Creature is not null)
                _visible[reference] = (actor.InstanceVersion, SacredCreatureHealthDisplay.Hover(
                    _visible.TryGetValue(reference, out var current) && current.Instance == actor.InstanceVersion ? current.Remaining : 0));
        }
    }
    private static uint Fade(uint color, uint alpha) => (color & 0xFFFFFF) | ((color >> 24) * alpha / 255) << 24;
}
