using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Core.World;
using Sacred.Core.World.Sector;
using Sacred.World;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Resolves ground-floor membership by coordinates and preserves explicit upper-floor traversal.</summary>
internal sealed class IndoorTraversalController(WorldStreamer worldStreamer, IndoorSceneState state)
{
    private (WorldTile Tile, byte? SurfaceLevel)? _pendingReset;

    public byte SurfaceLevel => state.ActiveGroup?.SurfaceLevel ?? _pendingReset?.SurfaceLevel ?? 0;

    public bool Update(Vector2 playerPosition)
    {
        var tile = WorldTile.From(playerPosition);
        var previousGroup = state.ActiveGroup;
        if (_pendingReset is { } pending)
        {
            state.ActiveGroup = FindInteriorGroup(tile, pending.SurfaceLevel);
            var sectorLoaded = worldStreamer.VisibleWorld.Sectors.Any(sector =>
                sector.Coord.X == tile.X / WorldStreamer.SectorTileCount &&
                sector.Coord.Y == tile.Y / WorldStreamer.SectorTileCount);
            if (state.ActiveGroup is { } restored)
            {
                _pendingReset = null;
                EngineLog.WriteLine($"Indoor surface restored after streaming: group {restored.Id}, level {restored.SurfaceLevel} at {tile.X},{tile.Y}.");
            }
            else if (sectorLoaded && (pending.SurfaceLevel is null or 0 or 1 || tile != pending.Tile))
                _pendingReset = null;
        }
        // Exterior (level 0) and the ground indoor grid (level 1) share the
        // same physical floor. No entrance crossing is required to select it.
        // Recheck even while stationary: streaming can supply its authored cells.
        if (SurfaceLevel <= 1)
        {
            state.ActiveGroup = FindInteriorGroup(tile, 1);
            var changed = previousGroup?.Id != state.ActiveGroup?.Id;
            if (changed)
                EngineLog.WriteLine($"Indoor coordinate check: group {state.ActiveGroup?.Id.ToString() ?? "exterior"}, level {SurfaceLevel} at {tile.X},{tile.Y}.");
            return changed;
        }
        // An upper floor can only change through stairs or a script transition.
        // Overlapping ground-floor doors never change its navigation domain.
        return previousGroup?.Id != state.ActiveGroup?.Id;
    }

    public void Reset(Vector2 playerPosition, byte? surfaceLevel = null)
    {
        // An ordinary teleport selects the ground floor, never an overlapping
        // upper grid. Stairs and scripted portals provide their destination level.
        surfaceLevel ??= 0;
        var tile = WorldTile.From(playerPosition);
        state.ActiveGroup = FindInteriorGroup(tile, surfaceLevel);
        _pendingReset = state.ActiveGroup is null ? (tile, surfaceLevel) : null;
        EngineLog.WriteLine(state.ActiveGroup is { } group
            ? $"Indoor surface reset: group {group.Id}, visibility state {group.SurfaceRenderLayer} at {tile.X},{tile.Y}."
            : $"Indoor surface reset: awaiting streamed tiles at {tile.X},{tile.Y}, requested level {surfaceLevel?.ToString() ?? "automatic"}.");
    }

    private IndoorTileGroup? FindInteriorGroup(WorldTile tile, byte? surfaceLevel = null)
    {
        if (surfaceLevel is null or 0)
            surfaceLevel = 1;
        var anchor = GroundAnchor(tile);
        var visited = new HashSet<IndoorTileGroupId>();
        foreach (var sector in worldStreamer.VisibleWorld.Sectors)
        foreach (var group in sector.IndoorTileGroups.Groups)
            if (visited.Add(group.Id) &&
                (!surfaceLevel.HasValue || group.SurfaceLevel == surfaceLevel.Value) &&
                (surfaceLevel > 1 || anchor is null || group.BuildingAnchor is null || group.BuildingAnchor == anchor) &&
                !IsEmptyExteriorCell(group, tile) &&
                IsInteriorZone(group, tile, anchor))
                return group;

        return null;
    }

    private (int X, int Y)? GroundAnchor(WorldTile tile)
    {
        var sector = worldStreamer.VisibleWorld.Sectors.FirstOrDefault(s =>
            s.Coord.X == tile.X / WorldStreamer.SectorTileCount &&
            s.Coord.Y == tile.Y / WorldStreamer.SectorTileCount);
        return sector?.IndoorAnchors?[tile.X % WorldStreamer.SectorTileCount, tile.Y % WorldStreamer.SectorTileCount];
    }

    private bool IsEmptyExteriorCell(IndoorTileGroup group, WorldTile tile)
    {
        if (group.SurfaceLevel > 1 || !group.TryGetLocalTile(tile.X, tile.Y, out var x, out var y) ||
            group.Presence[x, y])
            return false;
        var sector = worldStreamer.VisibleWorld.Sectors.FirstOrDefault(s =>
            s.Coord.X == tile.X / WorldStreamer.SectorTileCount &&
            s.Coord.Y == tile.Y / WorldStreamer.SectorTileCount);
        // The building anchor also covers exterior approaches inside its bounding
        // rectangle. Empty room cells replace the solid exterior building
        // volume (blocker A), not clear lanes or flyable exterior walls (B).
        return sector is not null &&
            sector.Pathing[tile.X % WorldStreamer.SectorTileCount, tile.Y % WorldStreamer.SectorTileCount].Properties.Behavior !=
                WldxTileBehavior.MovementBlockerA;
    }

    private static bool IsInteriorZone(IndoorTileGroup group, WorldTile tile, (int X, int Y)? anchor)
    {
        if (!group.TryGetLocalTile(tile.X, tile.Y, out var localX, out var localY) ||
            group.SurfaceLevel == 1 && !group.Presence[localX, localY] &&
            (anchor is null || anchor != group.BuildingAnchor))
            return false;

        // Indoor-grid records commonly leave the outdoor Indoor flag unset. The
        // authored 0x09/0x0A doorway pair, exposed by the packed enum, bounds the crossing.
        var pathing = group.Pathing[localX, localY];
        return !pathing.IsBlocked && !pathing.IsEntranceBoundary;
    }

    private readonly record struct WorldTile(int X, int Y)
    {
        public static WorldTile From(Vector2 position) =>
            new((int)MathF.Floor(position.X), (int)MathF.Floor(position.Y));
    }
}
