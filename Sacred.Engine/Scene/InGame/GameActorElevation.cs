using System;
using System.Numerics;
using Sacred.World;
using Sacred.World.Geometry;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Projects screen input onto the same elevated terrain surface used by the actor.</summary>
internal static class GameActorElevation
{
    private const int SurfaceRefinementIterations = 12;
    private const float SurfaceIntersectionTolerance = 0.01f;
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
        if (!TrySampleDifference(
                camera,
                elevation,
                screenPosition,
                viewportWidth,
                viewportHeight,
                MaximumTerrainHeight,
                out var upperDifference,
                out var upperWorldPosition))
        {
            return camera.ScreenToWorld(screenPosition, viewportWidth, viewportHeight);
        }

        if (MathF.Abs(upperDifference) <= SurfaceIntersectionTolerance)
            return upperWorldPosition;

        var upperHeight = MaximumTerrainHeight;
        for (var lowerHeight = MaximumTerrainHeight - SurfaceSearchStep;
             lowerHeight >= MinimumTerrainHeight;
             lowerHeight -= SurfaceSearchStep)
        {
            if (!TrySampleDifference(
                    camera,
                    elevation,
                    screenPosition,
                    viewportWidth,
                    viewportHeight,
                    lowerHeight,
                    out var lowerDifference,
                    out var lowerWorldPosition))
            {
                return camera.ScreenToWorld(screenPosition, viewportWidth, viewportHeight);
            }

            if (MathF.Abs(lowerDifference) <= SurfaceIntersectionTolerance)
                return lowerWorldPosition;

            // Scan from the highest possible surface toward the lowest. The first
            // negative-to-positive crossing is the visible surface, not the ground
            // underneath an elevated bridge.
            if (upperDifference < 0.0f && lowerDifference > 0.0f)
            {
                return RefineIntersection(
                    camera,
                    elevation,
                    screenPosition,
                    viewportWidth,
                    viewportHeight,
                    lowerHeight,
                    upperHeight);
            }

            upperHeight = lowerHeight;
            upperDifference = lowerDifference;
        }

        return camera.ScreenToWorld(screenPosition, viewportWidth, viewportHeight);
    }

    private static Vector2 RefineIntersection(
        SacredCamera camera,
        WorldElevationSampler elevation,
        Vector2 screenPosition,
        int viewportWidth,
        int viewportHeight,
        float lowerHeight,
        float upperHeight)
    {
        var worldPosition = Vector2.Zero;
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
                    out var difference,
                    out worldPosition))
            {
                break;
            }

            if (MathF.Abs(difference) <= SurfaceIntersectionTolerance)
                return worldPosition;

            if (difference > 0.0f)
                lowerHeight = middleHeight;
            else
                upperHeight = middleHeight;
        }

        var terrainHeight = (lowerHeight + upperHeight) * 0.5f;
        return camera.ScreenToWorld(
            AdjustScreenPosition(screenPosition, terrainHeight, camera.ViewportZoom),
            viewportWidth,
            viewportHeight);
    }

    private static bool TrySampleDifference(
        SacredCamera camera,
        WorldElevationSampler elevation,
        Vector2 screenPosition,
        int viewportWidth,
        int viewportHeight,
        float terrainHeight,
        out float difference,
        out Vector2 worldPosition)
    {
        worldPosition = camera.ScreenToWorld(
            AdjustScreenPosition(screenPosition, terrainHeight, camera.ViewportZoom),
            viewportWidth,
            viewportHeight);
        if (elevation.TrySampleHeight(worldPosition, out var sampledHeight))
        {
            difference = sampledHeight - terrainHeight;
            return true;
        }

        difference = 0.0f;
        return false;
    }

    private static Vector2 AdjustScreenPosition(Vector2 screenPosition, float worldHeight, float viewportZoom) =>
        TerrainElevationProjection.RemoveScreenOffset(screenPosition, worldHeight, viewportZoom);
}
