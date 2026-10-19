using System.Numerics;
using Sacred.Network.Gameplay;
using Sacred.Particles;
using Sacred.World.Geometry;
using Sacred.World.Networking;

namespace Sacred.World.Particles;

/// <summary>Independent playback for received cObjectFX instances. Item/actor models are attachment inputs.</summary>
public sealed class WorldObjectFxSystem
{
    private readonly Dictionary<uint, Playback> _playing = [];
    private readonly HashSet<uint> _visible = [];
    private readonly Dictionary<uint, string> _pending = [];
    public int ActiveCount => _playing.Values.Count(p => p.Started && p.Player.ActiveCount != 0);
    public bool IsActive(uint reference) => _playing.TryGetValue(reference, out var playback) && playback.Started && playback.Player.ActiveCount != 0;
    public IReadOnlyDictionary<uint, string> Pending => _pending;

    public void Synchronize(IEnumerable<GoldReplicatedObject> objects, Func<uint, WorldObjectFxAttachment?> attachment)
    {
        _visible.Clear();
        foreach (var obj in objects)
        {
            if (obj.Fx is not { IsSystemActive: true } fx || (obj.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) continue;
            var reference = obj.BaseState.ObjectReference;
            _visible.Add(reference);
            if (!_playing.TryGetValue(reference, out var current) || current.Instance != obj.InstanceVersion ||
                current.Type != obj.BaseState.ObjectType || current.Fx.FxType != fx.FxType)
                _playing[reference] = current = new(obj.InstanceVersion, obj.BaseState.ObjectType, fx);
            current.Fx = fx;
            current.Attachment = fx.WearerReference == 0 ? null : attachment(fx.WearerReference);
            if (current.Started && fx.WearerReference != 0 &&
                (current.Attachment is null || current.Attachment.InstanceVersion != current.WearerInstance))
                current.Player.Clear(); // An old FX instance must not attach to a reused item reference.
            current.Position = new Vector2(obj.BaseState.X, obj.BaseState.Y) / SacredNetworkPosition.NativeUnitsPerTile;
        }
        foreach (var reference in _playing.Keys.Where(id => !_visible.Contains(id)).ToArray())
        { _playing.Remove(reference); _pending.Remove(reference); }
    }

    internal void Update(SacredParticleCatalogue catalogue, float seconds, ParticleSimulationMode mode,
        IParticleGpuBackend? gpu, List<WorldParticle> particles, List<WorldGpuParticleBatch> batches)
    {
        foreach (var (reference, playback) in _playing)
        {
            var fx = playback.Fx;
            if (fx.WearerReference != 0 && playback.Attachment is null)
            {
                if (!playback.Started) _pending[reference] = "Waiting for the received wearer.";
                continue;
            }
            var anchor = playback.Attachment;
            var point = anchor?.Position ?? playback.Position;
            var nativeOffset = Vector3.TransformNormal(fx.WearingOffset, anchor?.Orientation ?? Matrix4x4.Identity);
            var offset = catalogue.Projection.Project(new(nativeOffset.X, nativeOffset.Y, 0));
            point += IsometricProjection.IsoToWorld(offset);
            var height = (anchor?.Height ?? 0) + nativeOffset.Z;
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(height) || !float.IsFinite(fx.Duration))
            { _pending[reference] = "FX state contains a non-finite placement or duration."; continue; }
            if (!playback.Started)
            {
                // Exact decoded script/event selectors only. Preview preset zero is not
                // a substitute for an unimplemented native event subtype.
                SacredParticleDefinition? definition = null;
                var preset = fx.FxType <= int.MaxValue && catalogue.TryGetEventDefinition(playback.Type, (int)fx.FxType, out definition)
                    ? (int?)fx.FxType : null;
                if (preset is null) catalogue.TryGetDefinition(playback.Type, out definition);
                if (definition is not { Status: SacredParticleDefinitionStatus.Decoded } ||
                    definition.UsesEventCornerColor || definition.Strength is not null)
                { _pending[reference] = $"Native FX subtype {fx.FxType} parameters are not decoded."; continue; }
                var finite = float.IsFinite(fx.Duration) && fx.Duration > 0;
                if (!playback.Player.Start(catalogue, playback.Type, !finite && !definition.OneTime,
                        anchor is not null, point, anchor is null ? height : 0, finite ? fx.Duration : 1,
                        preset, anchor?.ModelVertices, followSelf: anchor is not null, selfHeight: height,
                        actorBlockRadius: anchor?.BlockRadius, emissionLine: anchor?.EmissionLine))
                { _pending[reference] = "Waiting for decoded FX attachment inputs."; continue; }
                playback.Started = true;
                playback.WearerInstance = anchor?.InstanceVersion;
                _pending.Remove(reference);
                Console.WriteLine($"Network FX ready: ref {reference}, resource {playback.Type}, native subtype {fx.FxType}, wearer {fx.WearerReference}.");
            }
            playback.Player.SetObjectOrigin(point, height);
            playback.Player.GpuBackend = gpu;
            playback.Player.Update(seconds, point, height, mode, particles);
            playback.Player.CollectGpuBatches(batches);
        }
    }

    public void Clear() { _playing.Clear(); _pending.Clear(); _visible.Clear(); }
    internal void CollectGpuBatches(List<WorldGpuParticleBatch> batches)
    {
        foreach (var playback in _playing.Values) playback.Player.CollectGpuBatches(batches);
    }

    private sealed class Playback(long instance, uint type, GoldObjectFxState fx)
    {
        public long Instance { get; } = instance;
        public uint Type { get; } = type;
        public GoldObjectFxState Fx { get; set; } = fx;
        public WorldParticleEffectPlayer Player { get; } = new();
        public WorldObjectFxAttachment? Attachment { get; set; }
        public Vector2 Position { get; set; }
        public bool Started { get; set; }
        public long? WearerInstance { get; set; }
    }
}

/// <summary>Native model-camera attachment inputs; height uses native units before particle projection.</summary>
public sealed record WorldObjectFxAttachment(Vector2 Position, float Height, uint BlockRadius,
    IReadOnlyList<Vector3>? ModelVertices = null, ParticleEmissionLine? EmissionLine = null)
{
    public Matrix4x4 Orientation { get; init; } = Matrix4x4.Identity;
    public long InstanceVersion { get; init; }
}
