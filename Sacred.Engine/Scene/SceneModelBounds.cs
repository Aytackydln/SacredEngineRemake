using System;
using System.Numerics;
using Sacred.Granny.Animation;
using Sacred.Granny.Diagnostics;

namespace Sacred.Engine.Scene;

public readonly record struct SceneBoundsSnapshot(Vector3 Center, float Radius, ulong Revision);
public interface ISceneModelBounds { SceneBoundsSnapshot VisibilityBounds { get; } }

/// <summary>Caches bone-envelope evaluation per pose and world sphere per affine transform.
/// Revisions describe actual center/radius changes, independent of CPU vertex revisions.</summary>
internal sealed class SceneModelBounds(Vector3 initialCenter)
{
    private GrnPose? _pose;
    private ulong _poseRevision;
    private Matrix4x4 _transform;
    private float _staticRadius = -1;
    private Vector3 _minimum, _maximum;
    private SceneBoundsSnapshot _snapshot;
    public SceneBoundsSnapshot Get(GrnPose? pose, Matrix4x4 transform, float staticRadius)
    {
        var sourceChanged = !ReferenceEquals(_pose, pose);
        var poseChanged = sourceChanged || _poseRevision != (pose?.Revision ?? 0);
        if (!poseChanged && _transform == transform && _staticRadius == staticRadius) return _snapshot;
        var center = Vector3.Transform(initialCenter, transform);
        var radius = staticRadius;
        if (pose?.IsEvaluated == true)
        {
            using var measurement = AnimationPerformance.Measure(AnimationCpuStage.Bounds);
            if (poseChanged) (_minimum, _maximum) = pose.Data.Bounds.Evaluate(pose);
            var min = new Vector3(float.PositiveInfinity); var max = new Vector3(float.NegativeInfinity);
            for (var corner = 0; corner < 8; corner++)
            {
                var point = new Vector3((corner & 1) == 0 ? _minimum.X : _maximum.X,
                    (corner & 2) == 0 ? _minimum.Y : _maximum.Y, (corner & 4) == 0 ? _minimum.Z : _maximum.Z);
                point = Vector3.Transform(point, transform);
                min = Vector3.Min(min, point); max = Vector3.Max(max, point);
            }
            var boundsCenter = (min + max) * .5f;
            var boundsRadius = (max - min).Length() * .5f;
            if (Finite(boundsCenter) && float.IsFinite(boundsRadius))
            {
                center = boundsCenter;
                var rounding = MathF.Max(MathF.Abs(float.BitIncrement(center.X) - center.X),
                    MathF.Max(MathF.Abs(float.BitIncrement(center.Y) - center.Y), MathF.Abs(float.BitIncrement(center.Z) - center.Z)));
                radius = boundsRadius + 8 * rounding + MathF.Max(.0001f, boundsRadius * .00001f);
            }
            else radius = float.PositiveInfinity;
        }
        if (_snapshot.Revision == 0 || _snapshot.Center != center || _snapshot.Radius != radius)
            _snapshot = new(center, radius, _snapshot.Revision + 1);
        _pose = pose; _poseRevision = pose?.Revision ?? 0; _transform = transform; _staticRadius = staticRadius;
        return _snapshot;
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
