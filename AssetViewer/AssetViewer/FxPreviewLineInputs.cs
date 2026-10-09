using System;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Assets;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Explicit preview inputs until event-specific attachment resolution is recovered.
/// Uses a vertical line at the edge of the actor's mesh bounds so native small
/// particles remain visible; never guesses skeleton bone indices.</summary>
internal static class FxPreviewLineInputs
{
    public static ParticleEmissionLine Create(GrnAsset actor, SacredParticleProjection projection)
    {
        var positions = actor.Mesh?.Vertices.Select(v => v.Position).ToArray();
        if (positions is not { Length: > 0 }) throw new NotSupportedException("Line FX needs an actor mesh.");
        var min = positions.Aggregate(Vector3.Min); var max = positions.Aggregate(Vector3.Max);
        var center = (min + max) * .5f;
        center.X = max.X;
        Vector3 Local(float z) => new(center.X / projection.HorizontalScale,
            -center.Y / (MathF.Sqrt(2) * projection.GroundDepthFactor * projection.VerticalScale),
            z / (MathF.Sqrt(2) * projection.HeightFactor * projection.VerticalScale));
        return new(Local(min.Z), Local(max.Z));
    }
}
