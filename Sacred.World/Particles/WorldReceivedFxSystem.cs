using System.Numerics;
using Sacred.Network.Gameplay;
using Sacred.Particles;
using Sacred.World.Geometry;
using Sacred.World.Networking;

namespace Sacred.World.Particles;

/// <summary>Independent client-local creations. Receipt snapshots survive removal of the source object.</summary>
public sealed class WorldReceivedFxSystem
{
    private readonly Dictionary<long, Playback> _playing = [];
    private readonly Dictionary<long, string> _pending = [];
    private readonly List<WorldParticle> _discarded = [];
    private GoldConsumerCursor? _cursor;
    private long _session = -1;
    private long _localOrder;
    public int ActiveCount => _playing.Values.Count(p => p.Started && p.Player.ActiveCount > 0);
    public long StartedCount { get; private set; }
    public IReadOnlyDictionary<long, string> Pending => _pending;
    /// <summary>Retiring mesh bursts can still need a model which was loading at receipt.</summary>
    public IEnumerable<GoldReplicatedObject> AwaitingActors => _playing.Values
        .Where(p => !p.Started && p.Actor is null && p.Subject is not null &&
            SacredParticleCatalogue.LoadEmbedded().TryGetEventDefinition(p.Type, p.Preset, out var recipe) && recipe.RequiresActor)
        .Select(p => p.Subject!);

    /// <summary>Gold 541484..541556 creates 30C/event C locally when an ordinary corpse disappears.</summary>
    public bool EmitCorpseDisappearance(GoldReplicatedObject subject, WorldObjectFxAttachment anchor)
    {
        if (anchor.InstanceVersion != subject.InstanceVersion ||
            !SacredParticleCatalogue.LoadEmbedded().TryGetEventDefinition(0x30C, 0, out var recipe) ||
            SnapshotVertices(anchor.ModelVertices, recipe) is not { Length: > 0 } vertices) return false;
        var origin = new Vector3(anchor.Position * SacredNetworkPosition.NativeUnitsPerTile, anchor.Height);
        var position = new GoldObjectPosition(subject.BaseState.X, subject.BaseState.Y,
            subject.BaseState.Surface, subject.BaseState.Sector);
        _playing.Add(--_localOrder, new(0x30C, 0, position, origin, subject)
        { Actor = anchor with { ModelVertices = vertices } });
        Console.WriteLine($"Corpse disappearance FX queued: ref {subject.BaseState.ObjectReference}; posed vertices {vertices.Length}.");
        return true;
    }
    /// <summary>Shared CPU playback for headless consumers and deterministic packet verification.</summary>
    public void Advance(SacredParticleCatalogue catalogue, float seconds, List<WorldParticle> particles)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        Update(catalogue, seconds, ParticleSimulationMode.CpuSimd, null, particles, []);
    }

    public void Synchronize(GoldWorldReplica world, Func<GoldReplicatedObject, WorldObjectFxAttachment?> actor,
        Func<GoldObjectPosition, float> groundHeight)
    {
        _cursor ??= world.Consumers.CreateCursor("received-fx-creations", w =>
            w.Value is GoldCreatureInteractionWork { FxCreation: not null } or GoldFxBatchWork);
        if (_session != world.Consumers.Session)
        { _playing.Clear(); _pending.Clear(); StartedCount = 0; _session = world.Consumers.Session; }
        while (world.Consumers.TryPeek(_cursor, out var work))
        {
            var playback = work.Value switch
            {
                GoldCreatureInteractionWork { FxCreation: { } creation } interaction =>
                    new Playback(creation.Type, unchecked((int)creation.Parameter), creation.Position,
                        creation.ProjectionInput, interaction.Subject) { FollowSource = creation.EventId == 0x14 },
                // 181's +38 is the remaining chain count, not a preview selector. Only count one
                // has a decoded initializer/update with no subsequent chain allocation.
                GoldFxBatchWork batch when batch.FxType == 0x368 && batch.Parameter == 1 =>
                    new Playback(batch.FxType, 1, batch.Position, null, null) { NativeOrigin = batch.Vector1 },
                _ => null
            };
            if (playback is null)
            {
                world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Rejected,
                    "native freeze chain controller has no decoded playback yet");
                continue;
            }
            _playing.Add(work.Order, playback);
            world.Consumers.Report(_cursor, work, GoldConsumerOutcome.Consumed, "registered client-local FX creation");
        }
        foreach (var playback in _playing.Values)
        {
            if (playback.Started && !playback.FollowSource) continue;
            playback.Ground = groundHeight(playback.Position);
            // Capture posed vertices now: 14E can already have retired its source from the replica.
            // Never bind the request to a new object that happens to reuse the reference.
            if (playback.Subject is not { } subject) continue;
            SacredParticleCatalogue.LoadEmbedded().TryGetEventDefinition(playback.Type, playback.Preset, out var recipe);
            if (!playback.FollowSource && (recipe is null ||
                !recipe.RequiresActor && !recipe.UsesActorBlockRadius && recipe.LineEmission is null)) continue;
            if (playback.FollowSource && (!world.Objects.TryGetValue(subject.BaseState.ObjectReference, out var live) ||
                live.InstanceVersion != subject.InstanceVersion || (live.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0))
            { playback.Retiring = true; playback.Player.StopEmission(); continue; }
            if ((playback.Actor is null || playback.FollowSource) && actor(subject) is { } anchor && anchor.InstanceVersion == subject.InstanceVersion)
            {
                // Gold 5A556E: event +38 zero selects attachment-name entry 20 (head).
                if (playback.FollowSource) playback.Actor = anchor.BoneAttachment?.Invoke(20);
                else playback.Actor = anchor with { ModelVertices = SnapshotVertices(anchor.ModelVertices, recipe!) };
            }
        }
    }

    internal void Update(SacredParticleCatalogue catalogue, float seconds, ParticleSimulationMode mode,
        IParticleGpuBackend? gpu, List<WorldParticle> particles, List<WorldGpuParticleBatch> batches)
    {
        foreach (var (order, playback) in _playing.ToArray())
        {
            if (playback.Retiring && !playback.Started) { _playing.Remove(order); _pending.Remove(order); continue; }
            if (!playback.Started)
            {
                playback.WaitingSeconds += MathF.Max(0, seconds);
                if (!catalogue.TryGetEventDefinition(playback.Type, playback.Preset, out var definition) ||
                    definition.Status != SacredParticleDefinitionStatus.Decoded)
                { _pending[order] = $"No decoded event recipe for FX {playback.Type}, selector {playback.Preset}."; continue; }
                var anchor = playback.Actor;
                var position = playback.WorldOrigin is { } origin
                    ? new Vector2(origin.X, origin.Y) / SacredNetworkPosition.NativeUnitsPerTile
                    : new Vector2(playback.Position.X, playback.Position.Y) / SacredNetworkPosition.NativeUnitsPerTile;
                var height = playback.WorldOrigin?.Z ?? playback.Ground;
                if (playback.FollowSource)
                {
                    if (anchor is null) { _pending[order] = "Waiting for the received actor's head attachment."; continue; }
                    (position, height) = (anchor.Position, anchor.Height);
                }
                if (playback.NativeOrigin is { } native)
                {
                    var projected = catalogue.Projection.Project(new(native.X, native.Y, 0));
                    position = IsometricProjection.IsoToWorld(projected);
                    height = native.Z;
                }
                if (!playback.Player.Start(catalogue, playback.Type, false, anchor is not null,
                    position, height, eventPreset: playback.Preset, modelVertices: anchor?.ModelVertices,
                    actorBlockRadius: anchor?.BlockRadius, emissionLine: anchor?.EmissionLine))
                { _pending[order] = "Waiting for the received actor's posed geometry or decoded event inputs."; continue; }
                playback.Started = true; StartedCount++; _pending.Remove(order);
                playback.Player.GpuBackend = gpu;
                var catchUp = MathF.Max(0, playback.WaitingSeconds - seconds);
                while (catchUp > 0)
                {
                    var step = MathF.Min(.1f, catchUp); _discarded.Clear();
                    playback.Player.Update(step, default, 0, mode, _discarded); catchUp -= step;
                }
                _discarded.Clear();
                Console.WriteLine($"Received FX created: work {order}; resource {playback.Type}; selector {playback.Preset}; position {position}; height {height}.");
            }
            if (playback.FollowSource && playback.Actor is { } following)
                playback.Player.SetObjectOrigin(following.Position, following.Height);
            playback.Player.GpuBackend = gpu;
            playback.Player.Update(seconds, default, 0, mode, particles);
            playback.Player.CollectGpuBatches(batches);
            if (playback.Player.ActiveCount == 0) { _playing.Remove(order); _pending.Remove(order); }
        }
    }

    internal void CollectGpuBatches(List<WorldGpuParticleBatch> batches)
    { foreach (var playback in _playing.Values) playback.Player.CollectGpuBatches(batches); }
    private static Vector3[]? SnapshotVertices(IReadOnlyList<Vector3>? vertices, SacredParticleDefinition recipe)
    {
        if (vertices is null || recipe.ModelBurstCount == 0) return null;
        var count = Math.Min(vertices.Count, Math.Min(recipe.ModelBurstCount, recipe.Capacity));
        if (count <= 0) return null;
        // Preserve stdCreationFromActor's exact count/stride without skinning the unused mesh vertices.
        var stride = Math.Max(1, vertices.Count / count);
        return Enumerable.Range(0, count).Select(i => vertices[i * stride]).ToArray();
    }
    public void Clear() { _playing.Clear(); _pending.Clear(); _discarded.Clear(); _cursor = null; _session = -1; StartedCount = 0; }

    private sealed class Playback(uint type, int preset, GoldObjectPosition position,
        Vector3? worldOrigin, GoldReplicatedObject? subject)
    {
        public uint Type { get; } = type;
        public int Preset { get; } = preset;
        public GoldObjectPosition Position { get; } = position;
        public Vector3? WorldOrigin { get; } = worldOrigin;
        public Vector3? NativeOrigin { get; init; }
        public GoldReplicatedObject? Subject { get; } = subject;
        public WorldObjectFxAttachment? Actor { get; set; }
        public bool FollowSource { get; init; }
        public bool Retiring { get; set; }
        public float Ground { get; set; }
        public float WaitingSeconds { get; set; }
        public bool Started { get; set; }
        public WorldParticleEffectPlayer Player { get; } = new();
    }
}
