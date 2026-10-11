using System.Numerics;
using Sacred.Particles;
using Sacred.World.Geometry;

namespace Sacred.World.Particles;

/// <summary>Projects Plong's manual particle vector into the shared Viewer/world/Terminal draw buffer.</summary>
internal sealed class WorldPlongEffectPlayback
{
    private readonly SacredPlongFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly SacredParticleDrawDefinition _draw;
    private readonly ParticleSpriteReference _sprite;
    private readonly Vector2 _origin;
    private readonly float _height;
    private readonly int _id;

    public WorldPlongEffectPlayback(SacredParticleDefinition definition, SacredParticleProjection projection,
        Vector2 origin, float height, int id, uint seed)
    {
        _projection = projection; _origin = origin; _height = height; _id = id;
        _playback = new(definition.Plong!, definition.Capacity, seed);
        _draw = definition.Draw!;
        _sprite = new(_draw.TextureName, _draw.AtlasSide, _draw.AtlasSide,
            _draw.AtlasSide * _draw.AtlasSide, 1, ParticleShaderKind.ItemParticle);
    }
    public bool IsAlive => _playback.Active;
    public void Update(float seconds, List<WorldParticle> output)
    {
        _playback.Advance(seconds, new(0, 0, _height));
        if (!IsAlive) return;
        var state = _playback.CaptureState();
        var colors = new ParticleCornerColors(state.CornerColor0, state.CornerColor1, state.CornerColor2, state.CornerColor3);
        var index = 0;
        foreach (var p in _playback.Particles)
        {
            // stdRender rejects nonpositive size or energy independently of its alpha flags.
            if (p.HalfSize <= 0 || p.Energy <= 0) continue;
            var ground = IsometricProjection.IsoToWorld(_projection.Project(new(p.Position.X, p.Position.Y, 0)));
            var height = p.Position.Z * _projection.HeightFactor * _projection.VerticalScale;
            output.Add(new(_id, _sprite, _origin.X + ground.X, _origin.Y + ground.Y,
                height, 2 * p.HalfSize * _projection.HorizontalScale, (colors.Color0 >> 24) / 255f, index++)
            {
                Color = colors.Color0, CornerColors = colors,
                RenderHeight = 2 * p.HalfSize * _projection.VerticalScale,
                Rotation = _draw.UsesRotation ? p.Rotation : 0,
                Additive = (_draw.RawFlags & 1) != 0,
                PainterDepthKey = WorldPainterDepth.FromWorld(_origin + ground) + height / IsometricProjection.StepWidth
            });
        }
    }
}
