using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Native five-vertex line strips, with shared initialization/integration and vertex RGBA.</summary>
internal sealed class FxDeflectionPreview
{
    private readonly SacredDeflectionFxDefinition _definition;
    private readonly SacredParticleProjection _projection;
    private readonly Vector3 _origin;
    private readonly uint _seed;
    private readonly FxPreviewDraw[] _draws;
    private Vector3[] _positions = [], _velocities = [];
    private float _age;
    public FxDeflectionPreview(SacredDeflectionFxDefinition definition, SacredParticleProjection projection,
        Vector3 origin, uint seed)
    {
        (_definition, _projection, _origin, _seed) = (definition, projection, origin, seed);
        var count = definition.StreakCount * definition.TrailPoints;
        Mesh = new(new VertexPositionNormalTexture[count], Enumerable.Range(0, count).Select(i => checked((ushort)i)).ToArray());
        _draws = Enumerable.Range(0, definition.StreakCount).Select(streak => new FxPreviewDraw {
            IndexStart = streak * definition.TrailPoints, IndexCount = definition.TrailPoints,
            LineStrip = true, Untextured = true, Mode = ParticleTextureMode.NativeFxSurfaceRgba }).ToArray();
        Restart();
    }
    public Mesh Mesh { get; }
    public bool Active => _age * _definition.LifeLoss < _definition.InitialLife;
    public IReadOnlyList<FxPreviewDraw> Draws => Active ? _draws : [];
    public void Restart()
    {
        var random = new Random(unchecked((int)_seed));
        (_positions, _velocities) = SacredDeflectionFxPlayback.InitializeBurst(_definition, () => random.Next(32768));
        _age = 0;
        Advance(0, false);
    }
    public void Advance(float seconds, bool replay)
    {
        if (replay && _age >= 2) Restart();
        var life = _definition.InitialLife - _age * _definition.LifeLoss;
        _age += seconds;
        if (!Active) return;
        for (var index = 0; index < _positions.Length; index++)
        {
            var next = SacredDeflectionFxPlayback.AdvancePoint(_positions[index], _velocities[index], life, seconds, _definition);
            _positions[index] = next.Position; _velocities[index] = next.Velocity;
            var point = _origin + next.Position;
            var position = new Vector3(point.X * _projection.HorizontalScale,
                -point.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
                point.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
            var color = _definition.Colors[index % _definition.TrailPoints];
            Mesh.Vertices[index] = new(position, new(color & 0xFFFFFF, 0, (color >> 24) / 255f), Vector2.Zero);
        }
        Mesh.NotifyVerticesChanged();
    }
}
