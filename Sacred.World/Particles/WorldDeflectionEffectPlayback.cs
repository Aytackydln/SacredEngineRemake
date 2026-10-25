using System.Numerics;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>Shared 305 line-strip playback for attached requests and serialized FX objects.</summary>
internal sealed class WorldDeflectionEffectPlayback
{
    private readonly SacredDeflectionFxDefinition _definition;
    private readonly SacredParticleProjection _projection;
    private readonly long _generation;
    private readonly bool _follow;
    private readonly Vector3[] _points, _velocities;
    private Vector2 _position;
    private float _height, _age;
    public bool Active => _age * _definition.LifeLoss < _definition.InitialLife;
    public WorldDeflectionEffectPlayback(SacredDeflectionFxDefinition definition, SacredParticleProjection projection,
        long generation, Vector2 position, float height, bool follow)
    {
        (_definition, _projection, _generation, _position, _height, _follow) = (definition, projection, generation, position, height, follow);
        (_points, _velocities) = SacredDeflectionFxPlayback.InitializeBurst(definition, () => Random.Shared.Next(32768));
    }
    public void SetOrigin(Vector2 position, float height) => (_position, _height) = (position, height);
    public void Update(float seconds, Vector2 position, float height, List<WorldParticle> output)
    {
        if (_follow) SetOrigin(position, height);
        var life = _definition.InitialLife - _age * _definition.LifeLoss;
        _age += seconds;
        if (!Active) return;
        for (var i = 0; i < _points.Length; i++)
        {
            var next = SacredDeflectionFxPlayback.AdvancePoint(_points[i], _velocities[i], life, seconds, _definition);
            _points[i] = next.Position; _velocities[i] = next.Velocity;
        }
        var writer = new WorldFxSpriteWriter(new(_generation, 0x305, 0, _position, _height, _age, 1), _projection, output);
        for (var streak = 0; streak < _definition.StreakCount; streak++)
        for (var point = 0; point < _definition.TrailPoints - 1; point++)
        {
            var a = _points[streak * _definition.TrailPoints + point];
            var b = _points[streak * _definition.TrailPoints + point + 1];
            var delta = _projection.Project(b) - _projection.Project(a);
            writer.Add(string.Empty, delta.Length() / (2 * _projection.HorizontalScale),
                .5f / (_projection.HeightFactor * _projection.VerticalScale), uint.MaxValue, (a + b) * .5f,
                false, MathF.Atan2(delta.Y, delta.X),
                new(_definition.Colors[point], _definition.Colors[point], _definition.Colors[point + 1], _definition.Colors[point + 1]),
                untextured: true, additive: false);
        }
    }
}
