using System.Numerics;
using Sacred.Particles;
using Sacred.World.Geometry;

namespace Sacred.World.Particles;

/// <summary>Decoded sprite/ground flare layers at gameplay-owned projectile positions and ages.</summary>
public sealed class WorldSpriteFlareEffects
{
    private readonly Dictionary<uint, Playback> _playing = [];
    private readonly HashSet<uint> _visible = [];
    public int ActiveCount => _playing.Count;
    public void Synchronize(IEnumerable<WorldSpriteFlareEffect> effects)
    {
        _visible.Clear();
        foreach (var effect in effects)
        {
            _visible.Add(effect.Reference);
            if (!_playing.TryGetValue(effect.Reference, out var entry) || entry.State.Generation != effect.Generation)
                _playing[effect.Reference] = entry = new(effect);
            entry.State = effect;
        }
        foreach (var id in _playing.Keys.Where(id => !_visible.Contains(id)).ToArray()) _playing.Remove(id);
    }
    internal void Update(SacredParticleCatalogue catalogue, List<WorldParticle> particles)
    {
        foreach (var (reference, entry) in _playing)
        {
            var state = entry.State;
            if (!catalogue.TryGetEventDefinition(state.Type, state.EventPreset, out var definition) ||
                definition?.SpriteFlare is not { } flare) continue;
            if (entry.Visual is null || entry.Visual.Definition != flare)
            { entry.Visual = new(flare); entry.Age = 0; }
            var visual = entry.Visual;
            visual.RestoreState(visual.CaptureState() with { GroundFlareHeightOffset = state.Height - state.GroundHeight });
            visual.Advance(MathF.Max(0, state.Age - entry.Age));
            entry.Age = state.Age;
            if (!visual.Active) continue;
            var projection = catalogue.Projection;
            var order = 0;
            foreach (var surface in visual.Surfaces())
            {
                var layer = surface.Layer;
                var point = state.Position + IsometricProjection.IsoToWorld(projection.Project(new(layer.Offset.X, layer.Offset.Y, 0)));
                var height = (state.Height + layer.Offset.Z) * projection.HeightFactor * projection.VerticalScale;
                var sprite = new ParticleSpriteReference(layer.TextureName, 1, 1, 1, 1, ParticleShaderKind.ItemParticle);
                particles.Add(new(unchecked((int)reference), sprite, point.X, point.Y, height,
                    2 * layer.HalfSize * projection.HorizontalScale, (layer.Color >> 24) / 255f, order++)
                {
                    Color = layer.Color, RenderHeight = 2 * layer.HalfSize * projection.VerticalScale *
                        (surface.Billboard ? 1 : projection.GroundDepthFactor),
                    GroundAligned = !surface.Billboard, Additive = layer.Additive, SourceColorOnly = layer.SourceColorOnly,
                    PainterDepthKey = WorldPainterDepth.FromWorld(point) + height / IsometricProjection.StepWidth
                });
            }
        }
    }
    public void Clear() { _playing.Clear(); _visible.Clear(); }
    private sealed class Playback(WorldSpriteFlareEffect state)
    {
        public WorldSpriteFlareEffect State { get; set; } = state;
        public SacredSpriteFlareFxPlayback? Visual { get; set; }
        public float Age { get; set; }
    }
}

public sealed record WorldSpriteFlareEffect(uint Reference, long Generation, uint Type, int EventPreset,
    Vector2 Position, float Height, float GroundHeight, float Age);
