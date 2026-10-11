using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Particles;
using Sacred.World.Geometry;

namespace AssetViewer.AssetViewer;

/// <summary>Empty, unobstructed native world-X path for the recovered actor-burst caller.
/// The integer path walker steps before sampling and excludes the final endpoint.</summary>
internal static class FxPreviewCastTrail
{
    public static SacredParticleDefinition Emitter(SacredParticleCatalogue catalogue, SacredParticleCastTrailDefinition trail) =>
        catalogue.TryGetEventDefinition(trail.TypeId, trail.Preset, out var definition) ? definition
            : throw new NotSupportedException("The cast trail's native emitter is not decoded.");

    public static float MaximumDistance(SacredParticleCatalogue catalogue, SacredParticleDefinition definition) =>
        definition.CastTrail is { } trail
            ? Math.Min(FxPreviewParameters.MaximumDistance,
                Math.Min(64, ushort.MaxValue / 4 / Emitter(catalogue, trail).Capacity) * trail.WorldStep)
            : FxPreviewParameters.MaximumDistance;

    public static Vector3 NativePoint(SacredParticleCatalogue catalogue, float worldDistance)
    {
        var screen = IsometricProjection.WorldToIso(worldDistance / catalogue.WorldUnitsPerTile, 0);
        var p = catalogue.Projection;
        return new(screen.X / p.HorizontalScale, screen.Y / (p.VerticalScale * p.GroundDepthFactor), 0);
    }

    public static IReadOnlyList<Vector3> Origins(SacredParticleCatalogue catalogue, SacredParticleDefinition definition, float distance)
    {
        if (distance > MaximumDistance(catalogue, definition))
            throw new ArgumentOutOfRangeException(nameof(distance), "Cast distance exceeds the viewer's particle mesh capacity.");
        var length = (int)MathF.Round(distance);
        var step = definition.CastTrail!.WorldStep;
        var points = new List<Vector3>();
        for (var i = 0; i < length; i += step) points.Add(NativePoint(catalogue, i + 1));
        return points;
    }
}
