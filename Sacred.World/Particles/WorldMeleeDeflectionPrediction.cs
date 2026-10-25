using System.Numerics;
using Sacred.Network.Gameplay;
using Sacred.World.Networking;

namespace Sacred.World.Particles;

/// <summary>Cosmetic fallback for remote melee: Gold publishes positive damage, but no miss result.
/// A single body in front of a swing receives sparks after contact time without damage. This never changes HP.
/// Target selection and absent-damage inference are presentation policy, not decoded native results.</summary>
public sealed class WorldMeleeDeflectionPrediction
{
    private readonly Dictionary<long, Swing> _swings = [];
    private GoldConsumerCursor? _cursor;
    private long _session = -1;
    public long PredictedCount { get; private set; }

    public void Update(GoldWorldReplica world, float seconds,
        Func<GoldReplicatedObject, WorldObjectFxAttachment?> actor,
        Func<GoldCreatureAttackEvent, float?> duration,
        Action<GoldReplicatedObject> emit)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (_session != world.Consumers.Session)
        { _swings.Clear(); PredictedCount = 0; _session = world.Consumers.Session; }
        _cursor ??= world.Consumers.CreateCursor("melee-deflection-prediction", w => w.Value is GoldCombatNotification
            { Event: GoldCreatureAttackEvent or GoldCreatureDamageEvent });
        while (world.Consumers.TryPeek(_cursor, out var work))
        {
            var notice = (GoldCombatNotification)work.Value;
            if (notice is { Event: GoldCreatureAttackEvent { IsBow: false } attack, Subject: { } source })
                _swings[work.Order] = new(source, attack);
            else if (notice.Event is GoldCreatureDamageEvent damage)
                foreach (var swing in _swings.Values.Where(s => s.Attack.ObjectReference == damage.AttackerReference))
                    swing.Resolved = true; // Includes explicit zero damage, whose exact consumer emits the burst.
            world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Consumed, "observed cosmetic melee outcome");
        }
        foreach (var (order, swing) in _swings.ToArray())
        {
            swing.Age += seconds;
            if (swing.Resolved || swing.Age > 5 || !Alive(world, swing.Source) ||
                (!ReferenceEquals(world.Objects[swing.Attack.ObjectReference].Attack, swing.Attack) &&
                 (swing.Duration is null || swing.Age < swing.Duration)))
            {
                _swings.Remove(order); continue;
            }
            swing.Duration ??= duration(swing.Attack);
            if (swing.Duration is not { } length || !float.IsFinite(length) || length < 0) continue;
            // Wait through authored contact time and a receive grace period. Never infer a miss at receipt.
            if (swing.Age < length + .12f) continue;
            var target = Contact(world, swing, actor);
            if (target is null) Console.WriteLine($"Melee deflection: attacker {swing.Attack.ObjectReference}; completed without a unique visible body contact.");
            if (target is not null)
            {
                emit(target); PredictedCount++;
                Console.WriteLine($"Predicted melee deflection: attacker {swing.Attack.ObjectReference}; victim {target.BaseState.ObjectReference}; no damage received during swing.");
            }
            _swings.Remove(order);
        }
    }

    private static GoldReplicatedObject? Contact(GoldWorldReplica world, Swing swing,
        Func<GoldReplicatedObject, WorldObjectFxAttachment?> actor)
    {
        if (actor(swing.Source) is not { } source) return null;
        var direction = SacredNetworkFacing.ToTileDirection(swing.Attack.FacingBits);
        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y)) return null;
        GoldReplicatedObject? contact = null;
        foreach (var candidate in world.Objects.Values)
        {
            if (candidate.BaseState.ObjectReference == swing.Attack.ObjectReference || !Alive(world, candidate) ||
                candidate.BaseState.Surface != swing.Source.BaseState.Surface || actor(candidate) is not { } body) continue;
            var delta = body.Position - source.Position;
            var forward = Vector2.Dot(delta, direction);
            // Creature definitions commonly have no static block radius. Use the native ground-symbol
            // footprint default in that case; zero must not turn every creature into an empty collider.
            var bodyRadius = body.BlockRadius == 0 ? 50 : body.BlockRadius;
            var sourceRadius = source.BlockRadius == 0 ? 50 : source.BlockRadius;
            var radius = bodyRadius / SacredNetworkPosition.NativeUnitsPerTile;
            var reach = (sourceRadius + bodyRadius) * 2 / SacredNetworkPosition.NativeUnitsPerTile;
            if (forward <= 0 || forward > reach || MathF.Abs(delta.X * direction.Y - delta.Y * direction.X) > radius) continue;
            // Ambiguous crowds have no verified target; do not arbitrarily choose the closest actor.
            if (contact is not null) return null;
            contact = candidate;
        }
        return contact;
    }

    private static bool Alive(GoldWorldReplica world, GoldReplicatedObject snapshot) =>
        snapshot.Creature is not null && world.Objects.TryGetValue(snapshot.BaseState.ObjectReference, out var current) &&
        current.InstanceVersion == snapshot.InstanceVersion && current.CurrentHitPoints is > 0 &&
        (current.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) == 0;

    private sealed class Swing(GoldReplicatedObject source, GoldCreatureAttackEvent attack)
    {
        public GoldReplicatedObject Source { get; } = source;
        public GoldCreatureAttackEvent Attack { get; } = attack;
        public float Age;
        public float? Duration;
        public bool Resolved;
    }
}
