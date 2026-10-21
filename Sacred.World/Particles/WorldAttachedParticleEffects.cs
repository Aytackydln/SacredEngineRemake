using System.Numerics;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>Decoded finite effects whose native object origin follows an evaluated actor bone.</summary>
public sealed class WorldAttachedParticleEffects
{
    private readonly Dictionary<long, Playback> _playing = [];
    private readonly HashSet<long> _visible = [];
    private readonly List<WorldParticle> _discarded = [];
    public int ActiveCount => _playing.Values.Count(p => p.Started && (p.Surface?.Active == true || p.Player.ActiveCount > 0));

    public void Synchronize(IEnumerable<WorldAttachedParticleEffect> effects)
    {
        _visible.Clear();
        Append(effects);
        foreach (var id in _playing.Keys.Where(id => !_visible.Contains(id)).ToArray()) _playing.Remove(id);
    }

    private void Append(IEnumerable<WorldAttachedParticleEffect> effects)
    {
        foreach (var effect in effects)
        {
            _visible.Add(effect.Generation);
            if (!_playing.TryGetValue(effect.Generation, out var entry))
                _playing[effect.Generation] = entry = new(effect);
            entry.State = effect;
        }
    }

    internal void Update(SacredParticleCatalogue catalogue, ParticleSimulationMode mode,
        IParticleGpuBackend? gpu, List<WorldParticle> particles, List<WorldGpuParticleBatch> batches)
    {
        foreach (var playback in _playing.Values)
        {
            var state = playback.State;
            if (!playback.Started)
            {
                if (catalogue.TryGetEventDefinition(state.Type, state.Preset, out var definition) &&
                    definition.Status == SacredParticleDefinitionStatus.Decoded &&
                    (definition.LayeredFlare is not null || definition.StrikeSymbol is not null))
                {
                    playback.Surface = new(definition, state.DurationSeconds);
                    playback.Started = true;
                }
                else
                {
                    if (!playback.Player.Start(catalogue, state.Type, false, false,
                            state.Position, state.Height, eventPreset: state.Preset)) continue;
                    playback.Started = true;
                    playback.Player.SetClockScale(state.ClockScale);
                }
            }
            if (playback.Surface is { } surface)
            { surface.Update(state, catalogue.Projection, particles); continue; }
            playback.Player.SetObjectOrigin(state.Position, state.Height);
            playback.Player.GpuBackend = gpu;
            var remaining = MathF.Max(0, state.Age - playback.Age);
            // Catch up delayed asset loads with bounded simulation steps, publishing only the final frame.
            while (remaining > .1f)
            {
                _discarded.Clear();
                playback.Player.Update(.1f, state.Position, state.Height, mode, _discarded);
                remaining -= .1f;
            }
            playback.Player.Update(remaining, state.Position, state.Height, mode, particles);
            playback.Player.CollectGpuBatches(batches);
            playback.Age = state.Age;
        }
        _discarded.Clear();
    }
    internal void CollectGpuBatches(List<WorldGpuParticleBatch> batches)
    {
        foreach (var playback in _playing.Values) playback.Player.CollectGpuBatches(batches);
    }
    public void Clear() { _playing.Clear(); _visible.Clear(); }
    private sealed class Playback(WorldAttachedParticleEffect state)
    {
        public WorldAttachedParticleEffect State { get; set; } = state;
        public WorldParticleEffectPlayer Player { get; } = new();
        public bool Started { get; set; }
        public WorldAttachedSurfacePlayback? Surface { get; set; }
        public float Age { get; set; }
    }
}

public sealed record WorldAttachedParticleEffect(long Generation, uint Type, int Preset,
    Vector2 Position, float Height, float Age, float ClockScale = 1)
{
    public float? DurationSeconds { get; init; }
}
