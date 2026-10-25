using System.Numerics;
using Sacred.Network.Gameplay;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>Native client-local FX requests on any received wearer; no creature/resource allowlist.</summary>
public sealed class WorldCreatureFxSystem
{
    private readonly Dictionary<long, Playback> _playing = [];
    private readonly Dictionary<long, string> _pending = [];
    private long _received;
    private GoldConsumerCursor? _cursor;
    public int ActiveCount => _playing.Values.Count(p => p.Player.ActiveCount > 0);
    public int TrackedCount => _playing.Count;
    public IReadOnlyDictionary<long, string> Pending => _pending;
    public void Advance(SacredParticleCatalogue catalogue, float seconds, List<WorldParticle> particles)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        Update(catalogue, seconds, ParticleSimulationMode.CpuSimd, null, particles, []);
    }

    public void Synchronize(GoldWorldReplica world, Func<uint, ushort, WorldObjectFxAttachment?> attachment)
    {
        _cursor ??= world.Consumers.CreateCursor("attached-fx", work => work.Value is GoldAttachedFxRequest);
        if (_cursor.Session != world.Consumers.Session) { _playing.Clear(); _pending.Clear(); _received = 0; }
        var requests = new List<GoldAttachedFxRequest>();
        while (world.Consumers.TryPeek(_cursor, out var work))
        {
            requests.Add((GoldAttachedFxRequest)work.Value);
            world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Consumed, "registered independent particle playback; inspect server-fx for asset dependencies");
        }
        Synchronize(requests, attachment);
    }

    public void Synchronize(IEnumerable<GoldAttachedFxRequest> requests,
        Func<uint, ushort, WorldObjectFxAttachment?> attachment)
    {
        var received = _received;
        foreach (var request in requests.Where(r => r.Generation > received).OrderBy(r => r.Generation))
        {
            if (request.Generation <= _received) continue;
            _received = request.Generation;
            var binding = SacredFxAttachments.Find(request.Event.FxSubtype);
            ushort? bone = binding?.BoneFromSelector == true ? unchecked((ushort)request.Event.Selector) : binding?.Bone;
            if (bone is { } index && SacredFxAttachments.BoneName(index) is null) bone = null;
            _playing.Add(request.Generation, new(request, bone));
        }
        foreach (var playback in _playing.Values)
        {
            var owner = attachment(playback.Request.Event.WearerReference, ushort.MaxValue);
            if (owner is null || owner.InstanceVersion != playback.Request.WearerInstance)
            {
                // Missing model assets and missing/replaced logical wearers are distinguished by
                // the caller: the sentinel query supplies lifetime even before the model is ready.
                if (!playback.Retiring)
                    Console.WriteLine($"Creature FX draining: generation {playback.Request.Generation}; wearer {playback.Request.Event.WearerReference} retired or replaced.");
                playback.Retiring = true;
            }
            if (playback.Retiring) continue;
            var anchor = playback.Bone is { } bone ? attachment(playback.Request.Event.WearerReference, bone) : null;
            if (anchor is null && SacredFxAttachments.Find(playback.Request.Event.FxSubtype)?.FallbackBone is { } fallback)
                anchor = attachment(playback.Request.Event.WearerReference, fallback);
            if (anchor is not null || !playback.Started) playback.Anchor = anchor;
            playback.Neck = attachment(playback.Request.Event.WearerReference, 3) is not null;
        }
    }

    internal void Update(SacredParticleCatalogue catalogue, float seconds, ParticleSimulationMode mode,
        IParticleGpuBackend? gpu, List<WorldParticle> particles, List<WorldGpuParticleBatch> batches)
    {
        foreach (var (id, playback) in _playing.ToArray())
        {
            var request = playback.Request.Event;
            if (playback.Retiring)
            {
                playback.Player.StopEmission();
                _pending.Remove(id);
                if (!playback.Started || playback.Player.ActiveCount == 0) { _playing.Remove(id); continue; }
            }
            if (!playback.Started)
            {
                if (playback.Bone is null) { _pending[id] = $"Wearer {request.WearerReference}; resource {request.Resource}; native FX subtype {request.FxSubtype} attachment is pending."; continue; }
                if (playback.Anchor is null) { _pending[id] = $"Waiting for wearer bone {playback.Bone}."; continue; }
                if (!TryDefinition(catalogue, playback, out var definition))
                { _pending[id] = $"Native event selector {request.Selector} for resource {request.Resource} is pending."; continue; }
                // Native bone initialization (0x40) or following (0x200) replaces the initial vector.
                // Other branches still need their explicit directional/position consumer.
                if (request.Vector != Vector3.Zero && (definition.NativeAttachmentFlags & 0x240) == 0)
                { _pending[id] = "Native attached-FX vector operands are pending."; continue; }
                var anchor = playback.Anchor;
                if (!playback.Player.Start(catalogue, request.Resource, !definition.OneTime,
                    true, anchor.Position, 0, eventPreset: definition.Preset,
                    modelVertices: anchor.ModelVertices, followSelf: (definition.NativeAttachmentFlags & 0x200) != 0, selfHeight: anchor.Height,
                    actorBlockRadius: anchor.BlockRadius, emissionLine: anchor.EmissionLine))
                { _pending[id] = "Waiting for decoded actor inputs."; continue; }
                playback.Started = true;
                _pending.Remove(id);
                Console.WriteLine($"Creature FX ready: generation {id}; wearer {request.WearerReference}; resource {request.Resource}; subtype {request.FxSubtype}; selector {request.Selector}; bone {playback.Bone}.");
            }
            var current = playback.Anchor!;
            playback.Player.GpuBackend = gpu;
            playback.Player.Update(seconds, current.Position, current.Height, mode, particles);
            playback.Player.CollectGpuBatches(batches);
            if (playback.Player.ActiveCount == 0) { _playing.Remove(id); _pending.Remove(id); }
        }
    }

    private static bool TryDefinition(SacredParticleCatalogue catalogue, Playback playback,
        out SacredParticleDefinition definition)
    {
        var request = playback.Request.Event;
        definition = catalogue.EventDefinitions.FirstOrDefault(d => d.TypeId == request.Resource &&
            d.Status == SacredParticleDefinitionStatus.Decoded && d.SupportsAttachedEvent && d.Preset ==
            (d.EventSelectorOffset == 0x3C ? request.Selector : d.EventSelectorOffset == 0x38 ? playback.Neck ? 3 : 0 : 0))!;
        return definition is not null;
    }

    internal void CollectGpuBatches(List<WorldGpuParticleBatch> batches)
    {
        foreach (var playback in _playing.Values) playback.Player.CollectGpuBatches(batches);
    }
    public void Clear() { _playing.Clear(); _pending.Clear(); _received = 0; _cursor = null; }

    private sealed class Playback(GoldAttachedFxRequest request, ushort? bone)
    {
        public GoldAttachedFxRequest Request { get; } = request;
        public ushort? Bone { get; } = bone;
        public WorldParticleEffectPlayer Player { get; } = new();
        public WorldObjectFxAttachment? Anchor { get; set; }
        public bool Neck { get; set; }
        public bool Started { get; set; }
        public bool Retiring { get; set; }
    }
}
