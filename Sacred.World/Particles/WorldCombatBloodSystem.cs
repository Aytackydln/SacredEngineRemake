using Sacred.Network.Gameplay;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>Ordinary creature damage's blood burst, independent of 17A's optional elemental flash.</summary>
public sealed class WorldCombatBloodSystem
{
    private readonly Dictionary<long, Burst> _bursts = [];
    private readonly List<WorldParticle> _discarded = [];
    private GoldConsumerCursor? _cursor;
    private long _session = -1;
    public int ActiveCount => _bursts.Count;
    public long StartedCount { get; private set; }

    /// <param name="bloodColor">Null outside a renderable native sector or while original item data is unavailable.</param>
    public void Synchronize(GoldWorldReplica world, Func<GoldReplicatedObject, WorldObjectFxAttachment?> actor,
        Func<GoldReplicatedObject, uint?> bloodColor)
    {
        _cursor ??= world.Consumers.CreateCursor("combat-blood-fx", w => w.Value is GoldCombatNotification
            { Event: GoldCreatureDamageEvent });
        if (_session != world.Consumers.Session) { _bursts.Clear(); StartedCount = 0; _session = world.Consumers.Session; }
        while (world.Consumers.TryPeek(_cursor, out var work))
        {
            var notice = (GoldCombatNotification)work.Value;
            var damage = (GoldCreatureDamageEvent)notice.Event;
            if (notice.Subject is { Values.MaximumHitPoints: > 0 } subject &&
                notice.PreviousHitPoints is > 0 && damage.Damage > 0 && damage.Damage >= subject.Values.MaximumHitPoints / 50 &&
                bloodColor(subject) is { } color)
                _bursts.Add(work.Order, new(subject, color));
            world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Consumed, "evaluated ordinary damage blood threshold");
        }
        foreach (var (order, burst) in _bursts.ToArray())
        {
            // The scene calls this before retiring its models, preserving the receipt's final pose.
            if (!burst.Retiring && actor(burst.Subject) is { } source && source.InstanceVersion == burst.Subject.InstanceVersion)
            {
                // Gold 44CB80 selects neck attachment 3 when present, otherwise root 0.
                var anchor = source.BoneAttachment?.Invoke(3) ?? source.BoneAttachment?.Invoke(0);
                if (anchor is not null) burst.Anchor = anchor;
            }
            if (!world.Objects.TryGetValue(burst.Subject.BaseState.ObjectReference, out var live) ||
                live.InstanceVersion != burst.Subject.InstanceVersion ||
                (live.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0)
            {
                if (!burst.Started && burst.Anchor is null) _bursts.Remove(order);
                else { burst.Retiring = true; if (burst.Started) burst.Player.StopEmission(); }
                continue;
            }
        }
    }

    public void Advance(SacredParticleCatalogue catalogue, float seconds, List<WorldParticle> output) =>
        Update(catalogue, seconds, ParticleSimulationMode.CpuSimd, null, output, []);

    internal void Update(SacredParticleCatalogue catalogue, float seconds, ParticleSimulationMode mode,
        IParticleGpuBackend? gpu, List<WorldParticle> output, List<WorldGpuParticleBatch> batches)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        foreach (var (order, burst) in _bursts.ToArray())
        {
            burst.Player.GpuBackend = gpu;
            if (!burst.Started)
            {
                burst.Waiting += seconds;
                if (burst.Anchor is not { } initial) continue;
                if (!burst.Player.Start(catalogue, 0x304, false, true, initial.Position, 0,
                    eventPreset: 0, followSelf: true, selfHeight: initial.Height, eventColor: burst.Color)) continue;
                burst.Started = true; StartedCount++;
                Console.WriteLine($"Combat blood created: work {order}; victim {burst.Subject.BaseState.ObjectReference}; color {burst.Color:X8}; neck/root {initial.Position}, height {initial.Height}.");
                var remaining = MathF.Max(0, burst.Waiting - seconds);
                while (remaining > 0)
                {
                    var step = MathF.Min(remaining, .1f); _discarded.Clear();
                    burst.Player.Update(step, initial.Position, initial.Height, mode, _discarded); remaining -= step;
                }
            }
            var anchor = burst.Anchor!;
            burst.Player.Update(seconds, anchor.Position, anchor.Height, mode, output);
            if (burst.Retiring) burst.Player.StopEmission();
            burst.Player.CollectGpuBatches(batches);
            if (burst.Player.ActiveCount == 0) _bursts.Remove(order);
        }
        _discarded.Clear();
    }
    internal void CollectGpuBatches(List<WorldGpuParticleBatch> batches)
    { foreach (var burst in _bursts.Values) burst.Player.CollectGpuBatches(batches); }
    public void Clear() { _bursts.Clear(); _discarded.Clear(); _cursor = null; _session = -1; StartedCount = 0; }
    private sealed class Burst(GoldReplicatedObject subject, uint color)
    {
        public GoldReplicatedObject Subject { get; } = subject;
        public uint Color { get; } = color;
        public WorldObjectFxAttachment? Anchor { get; set; }
        public WorldParticleEffectPlayer Player { get; } = new();
        public bool Started { get; set; }
        public bool Retiring { get; set; }
        public float Waiting { get; set; }
    }
}
