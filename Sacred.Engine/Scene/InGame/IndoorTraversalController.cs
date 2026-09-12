using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Core.World.Sector;
using Sacred.World;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Switches the visible indoor grid when the player crosses one of its entrance cells.</summary>
internal sealed class IndoorTraversalController(WorldStreamer worldStreamer, IndoorSceneState state)
{
    private WorldTile? _lastTile;
    private IndoorTileGroup? _pendingEntranceGroup;

    public bool Update(Vector2 playerPosition)
    {
        var tile = WorldTile.From(playerPosition);
        if (_lastTile == tile)
            return false;
        _lastTile = tile;

        var entranceGroup = FindEntranceGroup(tile);
        if (entranceGroup is not null)
        {
            _pendingEntranceGroup = entranceGroup;
            return false;
        }

        if (_pendingEntranceGroup is not { } crossedGroup)
            return false;

        _pendingEntranceGroup = null;
        if (IsInteriorZone(crossedGroup, tile))
        {
            if (state.ActiveGroup is { } active && IsSameBuilding(active, crossedGroup))
                return false;

            state.ActiveGroup = crossedGroup;
            EngineLog.WriteLine($"Indoor entry: group {crossedGroup.Id} after gate at {tile.X},{tile.Y}");
            return true;
        }

        // A shared ground-floor doorway also exits an upper-floor group, but
        // only when leaving the building, not when backing away from the gate.
        if (state.ActiveGroup is { } activeGroup && IsSameBuilding(activeGroup, crossedGroup))
        {
            state.ActiveGroup = null;
            EngineLog.WriteLine($"Indoor exit: group {activeGroup.Id} through gate group {crossedGroup.Id} at {tile.X},{tile.Y}");
            return true;
        }

        return false;
    }

    public void Reset(Vector2 playerPosition, byte? surfaceLevel = null)
    {
        var tile = WorldTile.From(playerPosition);
        _lastTile = tile;
        _pendingEntranceGroup = null;
        state.ActiveGroup = FindInteriorGroup(tile, surfaceLevel);
        EngineLog.WriteLine(state.ActiveGroup is { } group
            ? $"Indoor surface reset: group {group.Id}, visibility state {group.SurfaceRenderLayer} at {tile.X},{tile.Y}."
            : $"Indoor surface reset: exterior at {tile.X},{tile.Y}.");
    }

    private IndoorTileGroup? FindEntranceGroup(WorldTile tile)
    {
        if (state.ActiveGroup is { } active && HasEntranceAt(active, tile))
            return active;

        var visited = new HashSet<IndoorTileGroupId>();
        foreach (var sector in worldStreamer.VisibleWorld.Sectors)
        foreach (var group in sector.IndoorTileGroups.Groups)
            if (visited.Add(group.Id) && HasEntranceAt(group, tile))
                return group;

        return null;
    }

    private IndoorTileGroup? FindInteriorGroup(WorldTile tile, byte? surfaceLevel = null)
    {
        var visited = new HashSet<IndoorTileGroupId>();
        foreach (var sector in worldStreamer.VisibleWorld.Sectors)
        foreach (var group in sector.IndoorTileGroups.Groups)
            if (visited.Add(group.Id) &&
                (surfaceLevel.HasValue || group.Entrances.Any()) &&
                (!surfaceLevel.HasValue || group.SurfaceLevel == surfaceLevel.Value) &&
                IsInteriorZone(group, tile))
                return group;

        return null;
    }

    private static bool HasEntranceAt(IndoorTileGroup group, WorldTile tile)
    {
        foreach (var trigger in group.Triggers)
            if (trigger.IsEntrance && trigger.WorldX == tile.X && trigger.WorldY == tile.Y)
                return true;
        return false;
    }

    private static bool IsInteriorZone(IndoorTileGroup group, WorldTile tile)
    {
        if (!group.TryGetAuthoredLocalTile(tile.X, tile.Y, out var localX, out var localY))
            return false;

        // Indoor-grid records commonly leave the outdoor Indoor flag unset. The
        // authored 0x09/0x0A doorway pair, exposed by the packed enum, bounds the crossing.
        return !group.Pathing[localX, localY].IsEntranceBoundary;
    }

    private static bool IsSameBuilding(IndoorTileGroup left, IndoorTileGroup right) =>
        left.WorldX == right.WorldX &&
        left.WorldY == right.WorldY &&
        left.Width == right.Width &&
        left.Height == right.Height;

    private readonly record struct WorldTile(int X, int Y)
    {
        public static WorldTile From(Vector2 position) =>
            new((int)MathF.Floor(position.X), (int)MathF.Floor(position.Y));
    }
}
