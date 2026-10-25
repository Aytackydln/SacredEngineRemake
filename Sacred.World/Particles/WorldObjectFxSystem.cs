using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Core.CombatArts;
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
    public long ReloadCount { get; private set; }
    public int ActiveCount => _playing.Values.Count(p => p.Started && (p.Surface?.Active == true || p.Player.ActiveCount != 0));
    public bool IsActive(uint reference) => _playing.TryGetValue(reference, out var playback) && playback.Started && (playback.Surface?.Active == true || playback.Player.ActiveCount != 0);
    public IReadOnlyDictionary<uint, string> Pending => _pending;
    public void Advance(SacredParticleCatalogue catalogue, float seconds, List<WorldParticle> particles)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        Update(catalogue, seconds, ParticleSimulationMode.CpuSimd, null, particles, []);
    }

    public void Synchronize(IEnumerable<GoldReplicatedObject> objects, Func<uint, WorldObjectFxAttachment?> attachment, Func<GoldObjectBaseState, float>? groundHeight = null)
    {
        _visible.Clear();
        foreach (var obj in objects)
        {
            if (obj.Fx is not { IsSystemActive: true } fx || (obj.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0) continue;
            var reference = obj.BaseState.ObjectReference;
            _visible.Add(reference);
            if (!_playing.TryGetValue(reference, out var current) || current.Instance != obj.InstanceVersion ||
                current.Type != obj.BaseState.ObjectType || current.Fx.FxType != fx.FxType || current.ReloadVersion != obj.FxReloadVersion)
            {
                _playing[reference] = current = new(obj.InstanceVersion, obj.BaseState.ObjectType, fx)
                { ReloadVersion = obj.FxReloadVersion, NotificationVersion = current?.Instance == obj.InstanceVersion ? current.NotificationVersion : 0 };
                ReloadCount++;
            }
            current.Fx = fx;
            current.Runtime = obj.SpecialFx;
            current.Attachment = fx.WearerReference == 0 ? null : attachment(fx.WearerReference);
            if (current.Started && fx.WearerReference != 0 &&
                (current.Attachment is null || current.Attachment.InstanceVersion != current.WearerInstance))
            {
                current.Player.Clear(); current.Surface = null; current.Retired = true;
                // An old FX instance must not attach to a reused item/actor reference.
            }
            current.GroundHeight = groundHeight?.Invoke(obj.BaseState) ?? 0;
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
            if (playback.Retired) continue;
            var fx = playback.Fx;
            if (fx.WearerReference != 0 && playback.Attachment is null)
            {
                if (!playback.Started) _pending[reference] = "Waiting for the received wearer.";
                continue;
            }
            var anchor = playback.Attachment;
            if (anchor?.BoneAttachment is { } findBone)
            {
                var bone = findBone(fx.AttachBone);
                if (bone is null)
                {
                    _pending[reference] = $"Waiting for received wearer bone {fx.AttachBone}.";
                    continue;
                }
                anchor = bone with { Orientation = anchor.Orientation };
            }
            var point = anchor?.Position ?? playback.Position;
            var nativeOffset = Vector3.TransformNormal(fx.WearingOffset, anchor?.Orientation ?? Matrix4x4.Identity);
            var offset = catalogue.Projection.Project(new(nativeOffset.X, nativeOffset.Y, 0));
            point += IsometricProjection.IsoToWorld(offset);
            var height = (anchor?.Height ?? playback.GroundHeight) + nativeOffset.Z;
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(height) || !float.IsFinite(fx.Duration))
            { _pending[reference] = "FX state contains a non-finite placement or duration."; continue; }
            _pending.Remove(reference);
            if (!playback.Started)
            {
                // Full cObjectFX state carries its CA rank/multiplier, not a particle preview selector.
                var visual = SacredCombatVisualCatalogue.Definitions.FirstOrDefault(d =>
                    d.Resource == playback.Type && d.FxSubtype == fx.FxType);
                if (visual is not null && catalogue.TryGetEventDefinition(playback.Type, 0, out var surface) &&
                    WorldAttachedParticleEffects.SupportsSurface(surface) && surface.VampireTransformation is null)
                {
                    var data = MemoryMarshal.Read<SacredCombatArtEffectDataLayout>(fx.EventData.Span);
                    var duration = visual.Duration(data.CombatArtLevel) * data.DurationMultiplier;
                    if (!float.IsFinite(duration) || duration <= 0 || surface.RotatingBlades is not null && anchor is null)
                    { _pending[reference] = "Waiting for native CA duration and wearer radius."; continue; }
                    playback.Surface = new(surface, duration, anchor?.BlockRadius, catalogue.Projection);
                    playback.Duration = duration;
                    playback.Started = true;
                    playback.WearerInstance = anchor?.InstanceVersion;
                    Console.WriteLine($"Network FX surface ready: ref {reference}, resource {playback.Type}, subtype {fx.FxType}, rank {data.CombatArtLevel}, duration {duration}.");
                }
                else
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
            }
            if (playback.Surface is { } direct)
            {
                playback.Age += MathF.Max(0, seconds);
                direct.Update(new(reference, playback.Type, 0, point, height, playback.Age)
                    { DurationSeconds = playback.Duration, ActorBlockRadius = anchor?.BlockRadius }, catalogue.Projection, particles);
                continue;
            }
            playback.Player.SetObjectOrigin(point, height);
            if (playback.Runtime is { ParticleFlags: { } flags } runtime && runtime.Version != playback.NotificationVersion)
            {
                if (playback.Player.SetParticleFlags(flags)) playback.NotificationVersion = runtime.Version;
                else _pending[reference] = $"Particle flag {flags} requires its native controller consumer.";
            }
            playback.Player.GpuBackend = gpu;
            playback.Player.Update(seconds, point, height, mode, particles);
            playback.Player.CollectGpuBatches(batches);
        }
    }

    public void Clear() { _playing.Clear(); _pending.Clear(); _visible.Clear(); ReloadCount = 0; }
    internal void CollectGpuBatches(List<WorldGpuParticleBatch> batches)
    {
        foreach (var playback in _playing.Values) playback.Player.CollectGpuBatches(batches);
    }

    private sealed class Playback(long instance, uint type, GoldObjectFxState fx)
    {
        public long Instance { get; } = instance;
        public long ReloadVersion { get; init; }
        public GoldSpecialFxRuntime? Runtime { get; set; }
        public long NotificationVersion { get; set; }
        public uint Type { get; } = type;
        public GoldObjectFxState Fx { get; set; } = fx;
        public WorldParticleEffectPlayer Player { get; } = new();
        public WorldObjectFxAttachment? Attachment { get; set; }
        public Vector2 Position { get; set; }
        public bool Started { get; set; }
        public long? WearerInstance { get; set; }
        public WorldAttachedSurfacePlayback? Surface { get; set; }
        public float GroundHeight { get; set; }
        public float Duration { get; set; }
        public float Age { get; set; }
        public bool Retired { get; set; }
    }
}

/// <summary>Native model-camera attachment inputs; height uses native units before particle projection.</summary>
public sealed record WorldObjectFxAttachment(Vector2 Position, float Height, uint BlockRadius,
    IReadOnlyList<Vector3>? ModelVertices = null, ParticleEmissionLine? EmissionLine = null)
{
    public Matrix4x4 Orientation { get; init; } = Matrix4x4.Identity;
    public long InstanceVersion { get; init; }
    public Func<ushort, WorldObjectFxAttachment?>? BoneAttachment { get; init; }
}
