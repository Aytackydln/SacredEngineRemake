using System.Numerics;
using Sacred.Particles;
using Sacred.World.Geometry;

namespace Sacred.World.Particles;

/// <summary>Decoded direct-surface particle families at an externally supplied animated bone.</summary>
internal sealed class WorldAttachedSurfacePlayback
{
    private readonly SacredParticleDefinition _definition;
    private readonly SacredLayeredFlareFxPlayback? _layers;
    private readonly SacredStrikeSymbolFxPlayback? _strike;
    private uint _random = 1;
    private float _age;

    public WorldAttachedSurfacePlayback(SacredParticleDefinition definition, float? duration)
    {
        _definition = definition;
        if (definition.LayeredFlare is { } layers)
            _layers = new(duration is > 0 ? layers with { InitialLifetime = duration.Value } : layers);
        if (definition.StrikeSymbol is { } strike) _strike = new(strike);
    }
    public bool Active => _layers?.Active ?? _strike?.Active ?? false;

    public void Update(WorldAttachedParticleEffect state, SacredParticleProjection projection, List<WorldParticle> output)
    {
        var remaining = MathF.Max(0, state.Age - _age);
        // Retain the native one-random-change-per-update controller during delayed attachment catch-up.
        while (remaining > 1f / 60)
        { Advance(1f / 60); remaining -= 1f / 60; }
        Advance(remaining);
        _age = state.Age;
        if (!Active) return;
        if (_layers is { } controller)
        {
            var order = 0;
            foreach (var layer in controller.Definition.Layers)
                Add(layer.TextureName, layer.HalfSize, layer.HalfSize, layer.Color, layer.Offset,
                    true, layer.Additive, layer.SourceColorOnly, -state.Age * layer.RotationRate, order++);
        }
        else if (_strike is { } strike && _definition.StrikeSymbol is { } definition)
        {
            var vertices = definition.SurfaceVertices;
            var min = new Vector3(vertices.Min(v => v.Position.X), vertices.Min(v => v.Position.Y), vertices.Min(v => v.Position.Z));
            var max = new Vector3(vertices.Max(v => v.Position.X), vertices.Max(v => v.Position.Y), vertices.Max(v => v.Position.Z));
            // The strip's native rectangular extent is retained. Arbitrary direct meshes remain separate families.
            Add(definition.Textures[strike.TextureFrame], (max.X - min.X) / 2, (max.Z - min.Z) * projection.HeightFactor / 2,
                uint.MaxValue, (min + max) / 2, false, true, false, 0, 0,
                // Native direct strip: bottom left/right, top left/right. World particles
                // use bottom left, top left, bottom right, top right.
                new(strike.SurfaceColor(0), strike.SurfaceColor(2), strike.SurfaceColor(1), strike.SurfaceColor(3)),
                strike.TextureFrame % 2);
            Add(definition.FlareTexture, definition.FlareHalfSize, definition.FlareHalfSize,
                strike.FlareColor, definition.FlareOffset, false, true, definition.FlareSourceColorOnly, 0, 1);
        }

        void Add(string texture, float halfWidth, float halfHeight, uint color, Vector3 offset,
            bool ground, bool additive, bool sourceColorOnly, float rotation, int order,
            ParticleCornerColors? corners = null, int? halfTexture = null)
        {
            var point = state.Position + IsometricProjection.IsoToWorld(projection.Project(new(offset.X, offset.Y, 0)));
            var height = (state.Height + offset.Z) * projection.HeightFactor * projection.VerticalScale;
            var sprite = new ParticleSpriteReference(texture, 1, halfTexture is null ? 1 : 2,
                halfTexture is null ? 1 : 2, 1, ParticleShaderKind.ItemParticle);
            output.Add(new(unchecked((int)state.Generation), sprite, point.X, point.Y, height,
                halfWidth * 2 * projection.HorizontalScale, (color >> 24) / 255f, order)
            {
                RenderHeight = halfHeight * 2 * projection.VerticalScale * (ground ? projection.GroundDepthFactor : 1),
                Color = color,
                CornerColors = corners,
                GroundAligned = ground,
                Additive = additive,
                SourceColorOnly = sourceColorOnly,
                Rotation = rotation,
                AtlasCell = halfTexture ?? 0,
                PainterDepthKey = WorldPainterDepth.FromWorld(point) + height / IsometricProjection.StepWidth
            });
        }
    }

    private void Advance(float seconds)
    {
        _layers?.Advance(seconds);
        _strike?.Advance(seconds, () =>
        {
            _random = unchecked(_random * 214013 + 2531011);
            return (int)(_random >> 16 & 0x7FFF);
        });
    }
}
