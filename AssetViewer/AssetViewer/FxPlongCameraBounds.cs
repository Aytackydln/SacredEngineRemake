using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Fixed birth/flight envelope keeps Plong's camera stable as the burst shrinks.</summary>
internal static class FxPlongCameraBounds
{
    public static IEnumerable<Vector3> Points(SacredPlongFxDefinition d, SacredParticleProjection p)
    {
        var lifetime = d.InitialHalfSize / d.ShrinkRate;
        var travel = d.HorizontalSpeed * lifetime;
        var verticalSpeed = Math.Max(0, d.VerticalSpeedWidth - d.VerticalSpeedOrigin);
        var top = d.SpawnHeight + verticalSpeed * verticalSpeed / (2 * d.Gravity);
        var halfSize = d.InitialHalfSize * Math.Max(p.HorizontalScale, p.VerticalScale);
        foreach (var x in new[] { -d.SpawnOrigin - travel, d.SpawnWidth - d.SpawnOrigin + travel })
        foreach (var y in new[] { -d.SpawnOrigin - travel, d.SpawnWidth - d.SpawnOrigin + travel })
        foreach (var z in new[] { 0f, top })
        {
            var center = new Vector3(x * p.HorizontalScale, -y * p.GroundDepthFactor * p.VerticalScale * MathF.Sqrt(2),
                z * p.HeightFactor * p.VerticalScale * MathF.Sqrt(2));
            yield return center - new Vector3(halfSize);
            yield return center + new Vector3(halfSize);
        }
    }
}
