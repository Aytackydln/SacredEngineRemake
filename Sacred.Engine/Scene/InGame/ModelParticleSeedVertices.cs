using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Granny.Diagnostics;
using Sacred.Particles;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Consumed synchronously by SeedModel: its existing count/stride selects which vertices to skin.</summary>
internal sealed class ModelParticleSeedVertices : IReadOnlyList<Vector3>
{
    private readonly SceneModelGeometry _geometry;
    private readonly Matrix4x4 _transform;
    private readonly Vector3 _origin;
    private readonly SacredParticleProjection _projection;
    public ModelParticleSeedVertices(SceneModel model, SacredParticleProjection projection)
    { _geometry = model.Geometry; _transform = model.Transform; _origin = model.RenderPosition; _projection = projection; Count = model.Geometry.BindMesh.Vertices.Length; }
    public int Count { get; }
    public Vector3 this[int index]
    {
        get
        {
            using var measurement = AnimationPerformance.Measure(AnimationCpuStage.ParticleSeeds);
            var localPosition = _geometry.Animation is { } animation ? animation.TransformPosition(index) : _geometry.BindMesh.Vertices[index].Position;
            var position = Vector3.Transform(localPosition, _transform) - _origin;
            return new(position.X / _projection.HorizontalScale,
                -position.Y / (MathF.Sqrt(2) * _projection.GroundDepthFactor * _projection.VerticalScale),
                position.Z / (MathF.Sqrt(2) * _projection.HeightFactor * _projection.VerticalScale));
        }
    }
    public IEnumerator<Vector3> GetEnumerator() { for (var i = 0; i < Count; i++) yield return this[i]; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
