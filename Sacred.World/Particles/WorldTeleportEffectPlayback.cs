using System.Numerics;
using Sacred.Particles;
using Sacred.World.Geometry;

namespace Sacred.World.Particles;

/// <summary>Projects manual native teleport particles into the normal world/viewer frame buffer.</summary>
internal sealed class WorldTeleportEffectPlayback
{
    private readonly TeleportParticlePlayback _playback;
    private readonly SacredParticleDefinition _definition;
    private readonly SacredParticleProjection _projection;
    private readonly ParticleSpriteReference _sprite;
    private readonly Vector2 _origin;
    private readonly float _height;
    private readonly int _id;

    public WorldTeleportEffectPlayback(SacredParticleDefinition definition, SacredParticleProjection projection,
        Vector2 origin, float height, int id)
    {
        _definition = definition; _projection = projection;
        _origin = origin; _height = height; _id = id;
        _playback = new(definition.Teleport!, definition.Capacity, (uint)Random.Shared.Next());
        var draw = definition.Draw!;
        _sprite = new(draw.TextureName, draw.AtlasSide, draw.AtlasSide,
            draw.AtlasSide * draw.AtlasSide, 1, ParticleShaderKind.ItemParticle);
    }

    public bool IsAlive => _playback.IsAlive;
    public void Update(float dt, List<WorldParticle> output)
    {
        _playback.Advance(dt);
        if (!IsAlive) return;
        var state = _playback.State;
        var colors = new ParticleCornerColors(state.CornerColor0, state.CornerColor1,
            state.CornerColor2, state.CornerColor3);
        var index = 0;
        foreach (var p in _playback.Particles)
        {
            // stdRender 0x761F5F rejects nonpositive energy even without ENERGY_ALPHA.
            if (p.HalfSize <= 0 || p.Energy <= 0) continue;
            var ground = IsometricProjection.IsoToWorld(_projection.Project(new(p.Position.X, p.Position.Y, 0)));
            var height = (_height + p.Position.Z) * _projection.HeightFactor * _projection.VerticalScale;
            output.Add(new(_id, _sprite, _origin.X + ground.X, _origin.Y + ground.Y,
                height, 2 * p.HalfSize * _projection.HorizontalScale, (colors.Color0 >> 24) / 255f, index++)
            {
                Color = colors.Color0, CornerColors = colors,
                RenderHeight = 2 * p.HalfSize * _projection.VerticalScale,
                Rotation = _definition.Draw!.UsesRotation ? p.Rotation : 0,
                Additive = (_definition.Draw.RawFlags & 1) != 0,
                PainterDepthKey = WorldPainterDepth.FromWorld(_origin + ground) + height / IsometricProjection.StepWidth
            });
        }
    }
}
