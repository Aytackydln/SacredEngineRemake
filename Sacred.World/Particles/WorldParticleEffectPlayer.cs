using System.Numerics;
using Sacred.Core.GameBin.Scripts;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>Interactive playback of decoded FX using the same simulator as script placements.
/// A timed preview controls emission only; surviving particles finish their decoded lifetime.
/// Self targeting samples a spawn origin. Following the actor is a separate preview override.</summary>
public sealed class WorldParticleEffectPlayer
{
    private readonly List<Playback> _playing = [];
    private readonly List<WorldTeleportEffectPlayback> _teleports = [];
    private int _nextId = -1;

    public IParticleGpuBackend? GpuBackend { get; set; }
    internal void CollectGpuBatches(List<WorldGpuParticleBatch> output)
    {
        for (var i = _playing.Count - 1; i >= 0; i--) _playing[i].Emitter.CollectGpuBatches(output);
    }
    internal void SetSimulationMode(ParticleSimulationMode mode)
    {
        foreach (var playback in _playing) playback.Emitter.SimulationMode = mode;
    }

    public int ActiveCount => _playing.Count + _teleports.Count;
    public bool IsEnabled(uint typeId, bool selfTarget, int? eventPreset = null) =>
        _playing.Any(p => p.TypeId == typeId && p.EventPreset == eventPreset && p.SelfTarget == selfTarget && p.Looping);

    public bool Start(SacredParticleCatalogue catalogue, uint typeId, bool looping,
        bool selfTarget, Vector2 point, float heightOffset, float emissionSeconds = 0.5f, int? eventPreset = null,
        IReadOnlyList<Vector3>? modelVertices = null, bool followSelf = false, float selfHeight = 0,
        uint? actorBlockRadius = null, ParticleEmissionLine? emissionLine = null, uint eventStrength = 0)
    {
        var found = eventPreset is { } preset
            ? catalogue.TryGetEventDefinition(typeId, preset, out var definition)
            : catalogue.TryGetDefinition(typeId, out definition);
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(heightOffset) || !float.IsFinite(selfHeight) ||
            !float.IsFinite(emissionSeconds) || emissionSeconds <= 0 ||
            !found || definition is null || (looping && definition.OneTime) ||
            definition.Status != SacredParticleDefinitionStatus.Decoded || definition.Draw is null)
            return false;
        if (definition.ModelBurstCount > 0 && (!selfTarget || modelVertices is not { Count: > 0 })) return false;
        if (definition.RequiresActorContext && !selfTarget) return false;
        if (definition.LineEmission is not null && emissionLine is not { IsFinite: true }) return false;
        definition = SacredParticleStrengthParameters.Resolve(definition, eventStrength);
        if (definition.UsesActorBlockRadius)
        {
            if (actorBlockRadius is not { } radius) return false;
            definition = SacredActorParticleParameters.Resolve(definition, radius);
        }
        if (looping && IsEnabled(typeId, selfTarget, eventPreset)) return true;
        // Bound interactive requests without changing any decoded spawn parameters.
        if (ActiveCount >= 64) return false;
        if (definition.Teleport is not null)
        {
            _teleports.Add(new(definition, catalogue.Projection, point,
                heightOffset + (selfTarget ? selfHeight : 0), _nextId--));
            return true;
        }
        var placement = new WorldParticleScriptPlacement(_nextId--,
            new SacredScriptCreateObject(null, typeId, null, null, null), definition, point.X, point.Y);
        var emitter = new WorldParticleEmitter(placement, catalogue.WorldUnitsPerTile, catalogue.Projection);
        if (!emitter.CanEmit) return false;
        emitter.SetOrigin(point, heightOffset + (selfTarget ? selfHeight : 0));
        if (emissionLine is { } line) emitter.SetLine(line);
        if (!definition.BurstOnFirstUpdate) emitter.EmitBurst();
        if (definition.ModelBurstCount > 0) emitter.SeedModel(modelVertices!);
        _playing.Add(new Playback(typeId, emitter, looping, selfTarget, selfTarget && followSelf,
            heightOffset, definition.EmissionDurationSeconds is not null || definition.OrbitEmission is not null
                ? float.PositiveInfinity : definition.OneTime ? 0 : emissionSeconds, eventPreset));
        return true;
    }

    public void Disable(uint typeId, bool selfTarget, int? eventPreset = null)
    {
        foreach (var playback in _playing)
            if (playback.TypeId == typeId && playback.EventPreset == eventPreset && playback.SelfTarget == selfTarget && playback.Looping)
            {
                playback.Looping = false;
                playback.Remaining = 0;
            }
    }

    public void Clear() { _playing.Clear(); _teleports.Clear(); }

    /// <summary>Updates native attachment inputs without resetting particles or release clocks.</summary>
    public void SetEmissionLine(uint typeId, int eventPreset, ParticleEmissionLine line)
    {
        foreach (var playback in _playing)
            if (playback.TypeId == typeId && playback.EventPreset == eventPreset) playback.Emitter.SetLine(line);
    }

    /// <summary>Advances standalone previews without constructing a world or executing scripts.
    /// Appends the current particles to the caller's frame buffer.</summary>
    public void Advance(float deltaSeconds, List<WorldParticle> output)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        Update(deltaSeconds, Vector2.Zero, 0, ParticleSimulationMode.CpuSimd, output);
    }

    internal void Update(float step, Vector2 selfPosition, float selfHeight,
        ParticleSimulationMode mode, List<WorldParticle> output)
    {
        for (var i = _teleports.Count - 1; i >= 0; i--)
        {
            _teleports[i].Update(step, output);
            if (!_teleports[i].IsAlive) _teleports.RemoveAt(i);
        }
        for (var i = _playing.Count - 1; i >= 0; i--)
        {
            var playback = _playing[i];
            if (playback.FollowSelf)
                playback.Emitter.SetOrigin(selfPosition, selfHeight + playback.HeightOffset);
            playback.Emitter.GpuBackend = GpuBackend;
            playback.Emitter.SimulationMode = mode;
            var emitting = playback.Looping || playback.Remaining > 0;
            playback.Emitter.Update(step, output, emitting);
            if (playback.Emitter.NativeRetired) { _playing.RemoveAt(i); continue; }
            playback.Remaining -= step;
            if (!playback.Looping && playback.Emitter.EmissionFinished) playback.Remaining = 0;
            if (!playback.Looping && playback.Remaining <= 0 && playback.Emitter.ParticleCount == 0)
                _playing.RemoveAt(i);
        }
    }

    private sealed class Playback(uint typeId, WorldParticleEmitter emitter, bool looping,
        bool selfTarget, bool followSelf, float heightOffset, float remaining, int? eventPreset)
    {
        public int? EventPreset { get; } = eventPreset;
        public uint TypeId { get; } = typeId;
        public WorldParticleEmitter Emitter { get; } = emitter;
        public bool Looping { get; set; } = looping;
        public bool SelfTarget { get; } = selfTarget;
        public bool FollowSelf { get; } = followSelf;
        public float HeightOffset { get; } = heightOffset;
        public float Remaining { get; set; } = remaining;
    }
}
