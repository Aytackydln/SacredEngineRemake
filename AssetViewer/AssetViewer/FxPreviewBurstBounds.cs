using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Particles;
using Sacred.World.Geometry;

namespace AssetViewer.AssetViewer;

internal static class FxPreviewBurstBounds
{
    public static IEnumerable<Vector3> Points(SacredParticleDefinition? definition,
        SacredParticleProjection projection, Vector3 origin, uint seed)
    {
        Vector3 ToModel(Vector3 point) => new(point.X * projection.HorizontalScale,
            -point.Y * projection.GroundDepthFactor * projection.VerticalScale * MathF.Sqrt(2),
            point.Z * projection.HeightFactor * projection.VerticalScale * MathF.Sqrt(2));
        if (definition?.Deflection is { } deflection)
        {
            var random = new Random(unchecked((int)seed));
            var (positions, velocities) = SacredDeflectionFxPlayback.InitializeBurst(deflection, () => random.Next(32768));
            var minimum = new Vector3(float.PositiveInfinity); var maximum = new Vector3(float.NegativeInfinity);
            for (var age = 0f; age < deflection.InitialLife / deflection.LifeLoss; age += 1f / 60)
            for (var index = 0; index < positions.Length; index++)
            {
                var point = ToModel(origin + positions[index]);
                minimum = Vector3.Min(minimum, point); maximum = Vector3.Max(maximum, point);
                var next = SacredDeflectionFxPlayback.AdvancePoint(positions[index], velocities[index],
                    deflection.InitialLife - age * deflection.LifeLoss, 1f / 60, deflection);
                positions[index] = next.Position; velocities[index] = next.Velocity;
            }
            yield return minimum - Vector3.One;
            yield return maximum + Vector3.One;
        }
        if (definition?.Teleport is not { } teleport) yield break;
        var extent = teleport.OrbitEnergy * Math.Max(projection.HorizontalScale, projection.VerticalScale);
        foreach (var x in new[] { -teleport.OrbitRadius, teleport.OrbitRadius })
        foreach (var y in new[] { -teleport.OrbitRadius, teleport.OrbitRadius })
        foreach (var z in new[] { 0f, 2 * teleport.BaseHeight })
        {
            var ground = IsometricProjection.IsoToWorld(projection.Project(new(x, y, 0)));
            var center = new Vector3(IsometricProjection.WorldToModel(ground.X, ground.Y),
                z * projection.HeightFactor * projection.VerticalScale * MathF.Sqrt(2));
            yield return center - new Vector3(extent);
            yield return center + new Vector3(extent);
        }
    }
}
