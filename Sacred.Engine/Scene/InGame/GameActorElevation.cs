using System;
using System.Numerics;
using Sacred.World;
using Sacred.World.Geometry;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Projects screen input onto the same elevated terrain surface used by the actor.</summary>
internal static class GameActorElevation
{
    private const int SurfaceRefinementIterations = 16;
    private const int HorizontalOffsetIterations = 6;
    private const float SurfaceIntersectionTolerance = 0.001f;
    private const float MinimumTerrainHeight = sbyte.MinValue * WorldElevationSampler.WorldHeightPerSample;
    private const float MaximumTerrainHeight = sbyte.MaxValue * WorldElevationSampler.WorldHeightPerSample;
    private const float SurfaceSearchStep = WorldElevationSampler.WorldHeightPerSample;

    public static Vector2 ScreenToWorldOnSurface(
        SacredCamera camera,
        WorldElevationSampler elevation,
        Vector2 screenPosition,
        int viewportWidth,
        int viewportHeight)
    {
        var found = false;
        var highestSurface = float.NegativeInfinity;
        var result = camera.ScreenToWorld(screenPosition, viewportWidth, viewportHeight);
        for (var initialHorizontalDirection = -1;
             initialHorizontalDirection <= 1;
             initialHorizontalDirection++)
        {
            if (!TryFindSurface(
                    camera,
                    elevation,
                    screenPosition,
                    viewportWidth,
                    viewportHeight,
                    initialHorizontalDirection,
                    out var worldPosition,
                    out var surfaceHeight) ||
                found && surfaceHeight <= highestSurface)
            {
                continue;
            }

            found = true;
            highestSurface = surfaceHeight;
            result = worldPosition;
        }

        return result;
    }

    private static bool TryFindSurface(
        SacredCamera camera,
        WorldElevationSampler elevation,
        Vector2 screenPosition,
        int viewportWidth,
        int viewportHeight,
        int initialHorizontalDirection,
        out Vector2 worldPosition,
        out float surfaceHeight)
    {
        var hasUpperSample = false;
        var upperHeight = 0.0f;
        var upperDifference = 0.0f;
        for (var candidateHeight = MaximumTerrainHeight;
             candidateHeight >= MinimumTerrainHeight;
             candidateHeight -= SurfaceSearchStep)
        {
            if (!TrySampleDifference(
                    camera,
                    elevation,
                    screenPosition,
                    viewportWidth,
                    viewportHeight,
                    candidateHeight,
                    initialHorizontalDirection,
                    out var difference,
                    out var candidateWorldPosition))
            {
                hasUpperSample = false;
                continue;
            }

            if (MathF.Abs(difference) <= SurfaceIntersectionTolerance)
            {
                worldPosition = candidateWorldPosition;
                surfaceHeight = candidateHeight;
                return true;
            }

            // Scan from the highest possible surface toward the lowest. The first
            // negative-to-positive crossing is the visible surface, not the ground
            // underneath an elevated bridge.
            if (hasUpperSample && upperDifference < 0.0f && difference > 0.0f)
            {
                return TryRefineIntersection(
                    camera,
                    elevation,
                    screenPosition,
                    viewportWidth,
                    viewportHeight,
                    candidateHeight,
                    upperHeight,
                    initialHorizontalDirection,
                    out worldPosition,
                    out surfaceHeight);
            }

            hasUpperSample = true;
            upperHeight = candidateHeight;
            upperDifference = difference;
        }

        worldPosition = default;
        surfaceHeight = 0.0f;
        return false;
    }

    private static bool TryRefineIntersection(
        SacredCamera camera,
        WorldElevationSampler elevation,
        Vector2 screenPosition,
        int viewportWidth,
        int viewportHeight,
        float lowerHeight,
        float upperHeight,
        int initialHorizontalDirection,
        out Vector2 worldPosition,
        out float surfaceHeight)
    {
        worldPosition = Vector2.Zero;
        for (var iteration = 0; iteration < SurfaceRefinementIterations; iteration++)
        {
            var middleHeight = (lowerHeight + upperHeight) * 0.5f;
            if (!TrySampleDifference(
                    camera,
                    elevation,
                    screenPosition,
                    viewportWidth,
                    viewportHeight,
                    middleHeight,
                    initialHorizontalDirection,
                    out var difference,
                    out worldPosition))
            {
                surfaceHeight = 0.0f;
                return false;
            }

            if (MathF.Abs(difference) <= SurfaceIntersectionTolerance)
            {
                surfaceHeight = middleHeight;
                return true;
            }

            if (difference > 0.0f)
                lowerHeight = middleHeight;
            else
                upperHeight = middleHeight;
        }

        surfaceHeight = (lowerHeight + upperHeight) * 0.5f;
        return TrySampleDifference(
            camera,
            elevation,
            screenPosition,
            viewportWidth,
            viewportHeight,
            surfaceHeight,
            initialHorizontalDirection,
            out _,
            out worldPosition);
    }

    private static bool TrySampleDifference(
        SacredCamera camera,
        WorldElevationSampler elevation,
        Vector2 screenPosition,
        int viewportWidth,
        int viewportHeight,
        float terrainHeight,
        int initialHorizontalDirection,
        out float difference,
        out Vector2 worldPosition)
    {
        worldPosition = camera.ScreenToWorld(
            AdjustScreenPosition(
                screenPosition,
                terrainHeight,
                terrainHeight * initialHorizontalDirection,
                camera.ViewportZoom),
            viewportWidth,
            viewportHeight);

        var sample = default(TerrainElevationSample);
        for (var iteration = 0; iteration < HorizontalOffsetIterations; iteration++)
        {
            if (!elevation.TrySample(worldPosition, out sample))
            {
                difference = 0.0f;
                return false;
            }

            var adjustedWorldPosition = camera.ScreenToWorld(
                AdjustScreenPosition(
                    screenPosition,
                    terrainHeight,
                    sample.HorizontalOffset,
                    camera.ViewportZoom),
                viewportWidth,
                viewportHeight);
            if (Vector2.DistanceSquared(adjustedWorldPosition, worldPosition) <= 0.000001f)
                break;
            worldPosition = adjustedWorldPosition;
        }

        if (!elevation.TrySample(worldPosition, out sample))
        {
            difference = 0.0f;
            return false;
        }

        difference = sample.Height - terrainHeight;
        return true;
    }

    private static Vector2 AdjustScreenPosition(
        Vector2 screenPosition,
        float worldHeight,
        float horizontalWorldOffset,
        float viewportZoom) =>
        TerrainElevationProjection.RemoveScreenOffset(
            screenPosition,
            worldHeight,
            horizontalWorldOffset,
            viewportZoom);
}
