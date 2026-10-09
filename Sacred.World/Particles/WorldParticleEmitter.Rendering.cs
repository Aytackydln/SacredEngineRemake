using System.Numerics;
using Sacred.Particles;
using Sacred.Particles.Diagnostics;
using Sacred.World.Geometry;

namespace Sacred.World.Particles;

internal sealed partial class WorldParticleEmitter
{
    private void AppendParticles(SacredParticleDefinition definition, bool showHalo, List<WorldParticle> output)
    {
        if (showHalo && _placement.Definition.Halo is { } halo) AddHalo(halo, output);
        foreach (var layer in definition.AdditionalHalos)
            if (showHalo || layer.PersistAfterEmission) AddHalo(layer, output);

        if (definition.ParticlesVisible)
        {
            AppendDraw(definition.Draw!, output);
            foreach (var draw in definition.AdditionalDraws) AppendDraw(draw, output);
        }
    }

    private void AppendDraw(SacredParticleDrawDefinition draw, List<WorldParticle> output)
    {
        var definition = _placement.Definition;
        var sprite = draw == definition.Draw ? _sprite : new ParticleSpriteReference(draw.TextureName,
            draw.AtlasSide, draw.AtlasSide, draw.AtlasSide * draw.AtlasSide, 1, ParticleShaderKind.ItemParticle);

        foreach (var batch in _batches)
        {
            if (batch.Gpu is not null) continue;
            for (var index = 0; index < batch.Count; index++)
            {
                var particle = batch[index];
                var parameters = batch.Parameters;
                if (particle.Fade <= 0 || particle.Size <= 0) continue;
                var local = particle.Position;
                var halfSize = particle.Size * draw.HalfSizeMultiplier;
                var ground = IsometricProjection.IsoToWorld(_projection.Project(new Vector3(local.X, local.Y, 0)));
                var color = draw.UsesParticleColor
                    ? particle.Color | ((uint)Math.Clamp((int)particle.Fade, 0, 255) << 24)
                    : WorldParticleAppearance.Color(draw, parameters, particle.Fade);
                var height = ((_heightOffset ?? _placement.Creation.HeightOffset ?? 0) + local.Z) *
                             _projection.HeightFactor * _projection.VerticalScale;
                var depthAnchor = _origin ?? (_placement.Creation.TilePosition is { } tile
                    ? new Vector2(tile.X, tile.Y)
                    : new Vector2(_placement.WorldX, _placement.WorldY));
                var origin = _origin ?? new Vector2(_placement.WorldX, _placement.WorldY);
                output.Add(new WorldParticle(
                    _placement.ScriptOffset,
                    sprite,
                    origin.X + ground.X,
                    origin.Y + ground.Y,
                    height + (draw.AnchoredAtBottom ? halfSize * _projection.VerticalScale : 0),
                    2 * halfSize * _projection.HorizontalScale,
                    (color >> 24) / InitialFade,
                    particle.DrawOrder)
                {
                    Color = color,
                    // Native pitch has cot(pitch)=2. Screen-space height therefore
                    // contributes half as much camera depth as ground displacement.
                    PainterDepthKey = WorldPainterDepth.FromWorld(depthAnchor + ground) +
                                      height / IsometricProjection.StepWidth,
                    AtlasCell = draw.UsesRandomAtlasCell ? particle.AtlasCell :
                        Math.Clamp((int)((InitialFade - particle.Fade) * sprite.FrameCount / 256), 0, sprite.FrameCount - 1),
                    Rotation = draw.UsesRotation ? particle.Rotation : 0,
                    Additive = definition.EmissionMode == 2 ? parameters.Index == 0 : (draw.RawFlags & 1) != 0,
                    SourceColorOnly = (draw.RawFlags & 0x10) != 0,
                    RenderHeight = 2 * halfSize * _projection.VerticalScale
                });
            }
        }
    }

    private void AddHalo(SacredParticleHaloDefinition halo, List<WorldParticle> output)
    {
        var haloColor = halo.ColorAt(_age);
        if ((haloColor >> 24) == 0) return;
        var frame = halo.AtlasAnimation?.FrameAt(_age);
        if (halo.AtlasAnimation is not null && frame is null) return;
        var halfSize = halo.HalfSizeAt(_age);
        using var scope = ParticlePerformance.Measure(ParticleCpuStage.Halos);
        var origin = _origin ?? new Vector2(_placement.WorldX, _placement.WorldY);
        origin += IsometricProjection.IsoToWorld(_projection.Project(new(halo.Offset.X, halo.Offset.Y, 0)));
        var height = ((_heightOffset ?? _placement.Creation.HeightOffset ?? 0) + halo.Offset.Z) *
                     _projection.HeightFactor * _projection.VerticalScale;
        output.Add(new WorldParticle(_placement.ScriptOffset,
            new ParticleSpriteReference(halo.TextureName, halo.AtlasSide, halo.AtlasSide, halo.AtlasSide * halo.AtlasSide, 1, ParticleShaderKind.ItemParticle),
            origin.X, origin.Y, height, 2 * halfSize * _projection.HorizontalScale,
            (haloColor >> 24) / InitialFade, -1)
        {
            Color = haloColor, Additive = halo.Additive, SourceColorOnly = halo.SourceColorOnly,
            Rotation = halo.Rotation, AtlasCell = frame ?? 0,
            RenderHeight = 2 * halfSize * _projection.VerticalScale,
            PainterDepthKey = WorldPainterDepth.FromWorld(origin) + height / IsometricProjection.StepWidth
        });
    }

}
