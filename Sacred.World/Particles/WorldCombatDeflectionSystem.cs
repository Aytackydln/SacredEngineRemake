using System.Numerics;
using Sacred.Network.Gameplay;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>Native deflection playback for explicit zero-damage 17A and local cosmetic melee predictions.</summary>
public sealed class WorldCombatDeflectionSystem
{
    private readonly Dictionary<long, Burst> _bursts = [];
    private GoldConsumerCursor? _cursor;
    private long _session = -1;
    private long _predictedOrder;
    public int ActiveCount => _bursts.Count;
    public long StartedCount { get; private set; }

    /// <summary>Local cosmetic result, deliberately separate from authoritative receive work identities.</summary>
    public void EmitPredicted(GoldReplicatedObject subject) => _bursts.Add(--_predictedOrder, new(subject));

    public void Synchronize(GoldWorldReplica world, Func<GoldReplicatedObject, WorldObjectFxAttachment?> actor,
        Func<GoldReplicatedObject, bool> visibleSector)
    {
        _cursor ??= world.Consumers.CreateCursor("combat-deflection-fx", w => w.Value is GoldCombatNotification
            { Event: GoldCreatureDamageEvent });
        if (_session != world.Consumers.Session) { _bursts.Clear(); StartedCount = 0; _session = world.Consumers.Session; }
        while (world.Consumers.TryPeek(_cursor, out var work))
        {
            var notice = (GoldCombatNotification)work.Value;
            if (notice.Event is GoldCreatureDamageEvent { Damage: 0 } && notice.PreviousHitPoints is > 0 &&
                notice.Subject is { } subject && visibleSector(subject))
                _bursts.Add(work.Order, new(subject));
            world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Consumed, "evaluated zero-damage deflection streaks");
        }
        foreach (var (order, burst) in _bursts.ToArray())
        {
            if (actor(burst.Subject) is { } source && source.InstanceVersion == burst.Subject.InstanceVersion)
                burst.Anchor = source.BoneAttachment?.Invoke(3) ?? source.BoneAttachment?.Invoke(0) ?? burst.Anchor;
            if (burst.Anchor is null && (!world.Objects.TryGetValue(burst.Subject.BaseState.ObjectReference, out var current) ||
                current.InstanceVersion != burst.Subject.InstanceVersion)) _bursts.Remove(order);
        }
    }

    public void Advance(SacredParticleCatalogue catalogue, float seconds, List<WorldParticle> output)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var definition = SacredDeflectionFxDefinition.Original;
        foreach (var (order, burst) in _bursts.ToArray())
        {
            if (burst.Points is null && burst.Anchor is not null)
            {
                burst.Initialize(definition); StartedCount++;
                Console.WriteLine($"Combat deflection created: work {order}; victim {burst.Subject.BaseState.ObjectReference}; streaks {definition.StreakCount}.");
            }
            burst.Age += seconds;
            if (burst.Age * definition.LifeLoss >= definition.InitialLife) { _bursts.Remove(order); continue; }
            if (burst.Points is null || burst.Anchor is not { } anchor) continue;
            var writer = new WorldFxSpriteWriter(new(order, 0x305, 0, anchor.Position, anchor.Height, burst.Age, 1), catalogue.Projection, output);
            for (var streak = 0; streak < definition.StreakCount; streak++)
            {
                for (var point = 0; point < definition.TrailPoints; point++)
                {
                    var index = streak * definition.TrailPoints + point;
                    var next = SacredDeflectionFxPlayback.AdvancePoint(burst.Points[index], burst.Velocities[index],
                        definition.InitialLife - (burst.Age - seconds) * definition.LifeLoss, seconds, definition);
                    burst.Points[index] = next.Position; burst.Velocities[index] = next.Velocity;
                }
                for (var point = 0; point < definition.TrailPoints - 1; point++)
                {
                    var a = burst.Points[streak * definition.TrailPoints + point];
                    var b = burst.Points[streak * definition.TrailPoints + point + 1];
                    var delta = catalogue.Projection.Project(b) - catalogue.Projection.Project(a);
                    var width = delta.Length();
                    // Native draws a one-pixel line strip. Shared renderers submit its segments as thin,
                    // untextured quads with the original vertex colours and camera-projected endpoints.
                    writer.Add(string.Empty, width / (2 * catalogue.Projection.HorizontalScale),
                        .5f / (catalogue.Projection.HeightFactor * catalogue.Projection.VerticalScale),
                        uint.MaxValue, (a + b) * .5f, false, MathF.Atan2(delta.Y, delta.X),
                        new(definition.Colors[point], definition.Colors[point], definition.Colors[point + 1], definition.Colors[point + 1]),
                        untextured: true, additive: false);
                }
            }
        }
    }

    public void Clear() { _bursts.Clear(); _cursor = null; _session = -1; StartedCount = 0; }

    private sealed class Burst(GoldReplicatedObject subject)
    {
        public GoldReplicatedObject Subject { get; } = subject;
        public WorldObjectFxAttachment? Anchor;
        public Vector3[]? Points;
        public Vector3[] Velocities = [];
        public float Age;
        public void Initialize(SacredDeflectionFxDefinition definition)
        {
            (Points, Velocities) = SacredDeflectionFxPlayback.InitializeBurst(definition, () => Random.Shared.Next(32768));
            // A late skeleton must not restart the short burst at its initial position.
            for (var elapsed = 0f; elapsed < Age;)
            {
                var seconds = MathF.Min(1f / 60, Age - elapsed);
                for (var i = 0; i < Points.Length; i++)
                {
                    var next = SacredDeflectionFxPlayback.AdvancePoint(Points[i], Velocities[i],
                        definition.InitialLife - elapsed * definition.LifeLoss, seconds, definition);
                    Points[i] = next.Position; Velocities[i] = next.Velocity;
                }
                elapsed += seconds;
            }
        }
    }
}
