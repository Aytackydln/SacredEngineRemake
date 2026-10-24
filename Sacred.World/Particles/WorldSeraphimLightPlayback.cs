using System.Numerics;
using Sacred.Core.GameBin.Scripts;
using Sacred.Particles;
using Sacred.Particles.Particles;

namespace Sacred.World.Particles;

/// <summary>Native HolyWall/HolyLight draw passes. Direct strips use projected vertical parallelograms.</summary>
internal sealed class WorldSeraphimLightPlayback
{
    private readonly SacredParticleDefinition _definition;
    private readonly float _duration;
    private readonly float _radius;
    private readonly WorldParticleEmitter? _emitter;
    private readonly List<WorldParticle> _discarded = [];
    private float _age, _angle, _rise, _decay = 1;
    private uint _random = 1;

    public WorldSeraphimLightPlayback(SacredParticleDefinition definition, float? duration, uint? radius,
        SacredParticleProjection projection)
    {
        _definition = definition;
        _duration = duration ?? definition.CelestialLight?.InitialLifetime ?? 0;
        _radius = radius == 0 ? definition.RotatingBlades?.DefaultActorRadius ?? 0 : radius ?? 0;
        if (definition.CelestialLight is not null)
        {
            var placement = new WorldParticleScriptPlacement(-1,
                new SacredScriptCreateObject(null, definition.TypeId, null, null, null), definition, 0, 0);
            _emitter = new(placement, 1, projection);
        }
    }
    public bool Active => _age <= _duration && (_definition.CelestialLight is not { } light || _decay > light.RetirementFactor);

    public void Update(WorldAttachedParticleEffect state, SacredParticleProjection projection, List<WorldParticle> output)
    {
        var remaining = MathF.Max(0, state.Age - _age);
        while (remaining > 1f / 60) { Advance(1f / 60, state); remaining -= 1f / 60; }
        Advance(remaining, state);
        if (!Active) return;
        var writer = new WorldFxSpriteWriter(state, projection, output);
        if (_definition.RotatingBlades is { } blades) DrawBlades(blades, writer);
        if (_definition.CelestialLight is { } light)
        {
            DrawColumn(light, writer);
            if (_emitter is not null)
            {
                var first = output.Count;
                _emitter.AppendCurrentParticles(output);
                // The native matrix scales XY by decay and keeps the vertical axis unchanged.
                for (var i = first; i < output.Count; i++)
                {
                    var p = output[i];
                    output[i] = p with { WorldX = state.Position.X + (p.WorldX - state.Position.X) * _decay,
                        WorldY = state.Position.Y + (p.WorldY - state.Position.Y) * _decay,
                        Size = p.Size * _decay, RenderHeight = p.RenderHeight * _decay };
                }
            }
        }
    }
    private void Advance(float seconds, WorldAttachedParticleEffect state)
    {
        if (seconds <= 0 || !Active) return;
        _age += seconds;
        if (_definition.RotatingBlades is { } blades)
        {
            _angle = (float)(_angle + (double)seconds * blades.RotationRate);
            if (_angle >= blades.RotationWrap) _angle -= blades.RotationWrap;
            if (_rise < blades.MaximumHeight) _rise = (float)(_rise + (double)seconds * blades.RiseRate);
        }
        if (_definition.CelestialLight is { } light)
        {
            if (_emitter is not null)
            {
                _emitter.SetOrigin(state.Position, state.Height);
                _emitter.SetCelestialBirthScale(light, _decay);
                _discarded.Clear(); _emitter.Update(seconds, _discarded, true);
            }
            _decay = (float)(_decay - (double)seconds / _duration);
        }
    }
    private void DrawBlades(SacredRotatingBladesFxDefinition d, WorldFxSpriteWriter writer)
    {
        writer.Add(d.GroundTexture, _radius + d.GroundMargin, _radius + d.GroundMargin, d.GroundColor, default, true, additive: d.GroundAdditive, sourceColorOnly: d.GroundSourceColorOnly);
        var vertices = d.TrailVertices;
        var size = MathF.Abs(vertices[0].Position.X);
        var colors = new ParticleCornerColors(vertices[0].Color, vertices[1].Color, vertices[2].Color, vertices[3].Color);
        Vector3 Point(float angle, float height) => new(MathF.Sin(angle) * _radius, MathF.Cos(angle) * _radius, height);
        for (var i = 0; i < d.RingSamples; i++)
            writer.Add(d.TrailTexture, size, size, uint.MaxValue, Point(i * d.RingAngleStep, 0), false, corners: colors, sourceColorOnly: d.TrailSourceColorOnly);
        for (var side = 1; side >= -1; side -= 2)
        for (var i = 0; i < d.HelixSamples; i++)
            writer.Add(d.TrailTexture, size, size, uint.MaxValue,
                Point(_angle + side * (i * d.SampleStep * d.HelixAngleScale + d.TipAngle), i * d.SampleStep * _rise), false, corners: colors, sourceColorOnly: d.TrailSourceColorOnly);
        _random = unchecked(_random * 214013 + 2531011);
        var tip = d.TipSizeBase + (_random >> 16 & 0x7FFF) * d.TipSizeRandomScale;
        writer.Add(d.TipTexture, tip, tip, uint.MaxValue, Point(_angle + d.TipAngle, _rise), false, sourceColorOnly: d.TrailSourceColorOnly);
        writer.Add(d.TipTexture, tip, tip, uint.MaxValue, Point(_angle - d.TipAngle, _rise), false, sourceColorOnly: d.TrailSourceColorOnly);
    }
    private void DrawColumn(SacredCelestialLightFxDefinition d, WorldFxSpriteWriter writer)
    {
        var brightness = MathF.Sin(_age * d.WaveRate) * d.WaveScale + d.WaveOrigin;
        uint Alpha(float factor, uint rgb) => (uint)Math.Clamp((int)factor, 0, 255) << 24 | rgb;
        for (var i = 0; i + 3 < d.ColumnVertices.Count; i += 2)
            Strip(d.ColumnVertices, i, Alpha(brightness * d.ColumnAlpha, 0xFFFFFF), null);
        for (var i = 0; i < d.EdgeVertices.Count; i += 4)
            Strip(d.EdgeVertices, i, null, Alpha(brightness * d.EdgeAlpha, 0xFFFFFF));
        writer.Add(d.GroundTexture, d.GroundHalfSize, d.GroundHalfSize, Alpha(_decay * 255, d.GroundRgb), default, true,
            rotation: -_age * d.GroundRotationRate, additive: d.FlareAdditive, sourceColorOnly: d.FlareSourceColorOnly);
        writer.Add(d.GlowTexture, d.GlowHalfSize, d.GlowHalfSize, Alpha(brightness * 255, d.GlowRgb), default, true, additive: d.FlareAdditive, sourceColorOnly: d.FlareSourceColorOnly);
        void Strip(IReadOnlyList<SacredFxSurfaceVertexLayout> vertices, int index, uint? bothBottom, uint? innerBottom)
        {
            var a = vertices[index]; var b = vertices[index + 1];
            var c = vertices[index + 2]; var e = vertices[index + 3];
            if (bothBottom is { } bottom) { a = a.WithColor(bottom); c = c.WithColor(bottom); }
            if (innerBottom is { } inner)
            { if (index == 0) c = c.WithColor(inner); else a = a.WithColor(inner); }
            var p = a.Position * new Vector3(_decay, _decay, 1);
            var q = c.Position * new Vector3(_decay, _decay, 1);
            if (p.X > q.X) { (p, q) = (q, p); (a, c) = (c, a); (b, e) = (e, b); }
            var width = q.X - p.X;
            if (width < .00001f) return;
            var height = b.Position.Z - a.Position.Z;
            writer.Add(d.GlowTexture, width / 2, height / 2,
                uint.MaxValue, (p + q) / 2 + new Vector3(0, 0, height / 2), false,
                corners: new(a.Color, b.Color, c.Color, e.Color), untextured: true,
                shear: (q.Y - p.Y) / width);
        }
    }
}


