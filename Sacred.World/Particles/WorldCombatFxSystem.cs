using Sacred.Network.Gameplay;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>17A's client-local event 59/416 creations, separate from creature damage processing.</summary>
public sealed class WorldCombatFxSystem
{
    private readonly Dictionary<long, Hit> _hits = [];
    private GoldConsumerCursor? _cursor;
    private long _session = -1;
    public int ActiveCount => _hits.Count;
    public long StartedCount { get; private set; }

    public void Synchronize(GoldWorldReplica world, Func<GoldReplicatedObject, WorldObjectFxAttachment?> actor)
    {
        _cursor ??= world.Consumers.CreateCursor("combat-hit-fx", w => w.Value is GoldCombatNotification
            { Event: GoldCreatureDamageEvent });
        if (_session != world.Consumers.Session)
        { _hits.Clear(); StartedCount = 0; _session = world.Consumers.Session; }
        while (world.Consumers.TryPeek(_cursor, out var work))
        {
            var notification = (GoldCombatNotification)work.Value;
            var damage = (GoldCreatureDamageEvent)notification.Event;
            if (damage.ImpactEffectEnabled != 0 && notification.Subject is { } subject)
                _hits.Add(work.Order, new(subject, unchecked((byte)damage.ImpactEffectData)));
            world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Consumed,
                damage.ImpactEffectEnabled == 0 ? "impact creation disabled by packet" : "registered victim-root hit flash");
        }
        foreach (var (order, hit) in _hits.ToArray())
        {
            if (hit.Anchor is not null) continue; // Native flag 40 initializes the root once; it does not follow.
            if (actor(hit.Subject) is { } source && source.InstanceVersion == hit.Subject.InstanceVersion &&
                source.BoneAttachment?.Invoke(0) is { } root)
                hit.Anchor = root;
            else if (!world.Objects.TryGetValue(hit.Subject.BaseState.ObjectReference, out var live) ||
                live.InstanceVersion != hit.Subject.InstanceVersion)
                _hits.Remove(order);
        }
    }

    public void Advance(SacredParticleCatalogue catalogue, float seconds, List<WorldParticle> output)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        foreach (var (order, hit) in _hits.ToArray())
        {
            if (!catalogue.TryGetEventDefinition(0x360, 0x416, out var recipe) || recipe.HitFlash is not { } definition)
                continue;
            if (hit.Texture >= definition.Textures.Count)
            {
                Console.WriteLine($"Combat hit FX skipped: native texture slot {hit.Texture} is outside the constructor table.");
                _hits.Remove(order); continue;
            }
            if (hit.Playback is null)
            {
                hit.Playback = new(definition); StartedCount++;
                Console.WriteLine($"Combat flash created: work {order}; victim {hit.Subject.BaseState.ObjectReference}; texture {definition.Textures[hit.Texture]}; root ready {hit.Anchor is not null}.");
            }
            hit.Playback.Advance(seconds);
            if (!hit.Playback.Active) { _hits.Remove(order); continue; }
            if (hit.Anchor is not { } anchor) continue;
            var state = new WorldAttachedParticleEffect(order, recipe.TypeId, recipe.Preset!.Value,
                anchor.Position, anchor.Height, hit.Playback.Age, 1);
            new WorldFxSpriteWriter(state, catalogue.Projection, output).Add(definition.Textures[hit.Texture],
                definition.HalfSize, definition.HalfSize, hit.Playback.Color, default, true,
                rotation: -hit.Playback.Age, additive: definition.Additive, sourceColorOnly: definition.SourceColorOnly);
        }
    }

    public void Clear() { _hits.Clear(); _cursor = null; _session = -1; StartedCount = 0; }

    private sealed class Hit(GoldReplicatedObject subject, byte texture)
    {
        public GoldReplicatedObject Subject { get; } = subject;
        public byte Texture { get; } = texture;
        public WorldObjectFxAttachment? Anchor { get; set; }
        public SacredHitFlashFxPlayback? Playback { get; set; }
    }
}
