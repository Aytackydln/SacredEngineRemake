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

    public int ActiveCount => _playing.Count;
    public bool IsEnabled(uint typeId, bool selfTarget, int? eventPreset = null) =>
        _playing.Any(p => p.TypeId == typeId && p.EventPreset == eventPreset && p.SelfTarget == selfTarget && p.Looping);

    public bool Start(SacredParticleCatalogue catalogue, uint typeId, bool looping,
        bool selfTarget, Vector2 point, float heightOffset, float emissionSeconds = 0.5f, int? eventPreset = null,
        IReadOnlyList<Vector3>? modelVertices = null, bool followSelf = false, float selfHeight = 0)
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
        if (looping && IsEnabled(typeId, selfTarget, eventPreset)) return true;
        // Bound interactive requests without changing any decoded spawn parameters.
        if (_playing.Count >= 64) return false;
        var placement = new WorldParticleScriptPlacement(_nextId--,
            new SacredScriptCreateObject(null, typeId, null, null, null), definition, point.X, point.Y);
        var emitter = new WorldParticleEmitter(placement, catalogue.WorldUnitsPerTile, catalogue.Projection);
        if (!emitter.CanEmit) return false;
        emitter.SetOrigin(point, heightOffset + (selfTarget ? selfHeight : 0));
        emitter.EmitBurst();
        if (definition.ModelBurstCount > 0) emitter.SeedModel(modelVertices!);
        _playing.Add(new Playback(typeId, emitter, looping, selfTarget, selfTarget && followSelf,
            heightOffset, definition.OneTime ? 0 : emissionSeconds, eventPreset));
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

    public void Clear() => _playing.Clear();

    internal void Update(float step, Vector2 selfPosition, float selfHeight,
        ParticleSimulationMode mode, List<WorldParticle> output)
    {
        for (var i = _playing.Count - 1; i >= 0; i--)
        {
            var playback = _playing[i];
            if (playback.FollowSelf)
                playback.Emitter.SetOrigin(selfPosition, selfHeight + playback.HeightOffset);
            playback.Emitter.GpuBackend = GpuBackend;
            playback.Emitter.SimulationMode = mode;
            var emitting = playback.Looping || playback.Remaining > 0;
            playback.Emitter.Update(step, output, emitting);
            playback.Remaining -= step;
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
