using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Core.World.Sector;
using Sacred.World;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Applies WLDX floor changes, including stairs without a treppe.bin zone.</summary>
internal sealed class IndoorStairsTraversalController(WorldStreamer worldStreamer)
{
    private IndoorTileGroupId? _arrivalGroup;
    private readonly HashSet<(int X, int Y)> _arrivalTiles = [];
    private IndoorStairTarget? _activated;

    public IEnumerable<WorldFocusTarget> FocusTargets(IndoorTileGroup? active, Vector2 origin, float range)
    {
        if (active is null) yield break;
        foreach (var stair in active.Triggers)
        {
            if (!stair.IsStairs || stair.Pathing.IsBlocked || IsArrival(active, stair.WorldX, stair.WorldY) ||
                Math.Abs(stair.WorldX + .5f - origin.X) > range || Math.Abs(stair.WorldY + .5f - origin.Y) > range ||
                FindDestination(active, stair) is null) continue;
            yield return new WorldFocusTarget(((uint)stair.WorldX << 16) | (uint)stair.WorldY,
                new Vector2(stair.WorldX + .5f, stair.WorldY + .5f), null)
                { IndoorStair = new(active.Id, stair) };
        }
    }

    public bool TryActivate(WorldFocusTarget target, Vector2 origin, byte surfaceLevel)
    {
        if (target.IndoorStair is not { } selected || Vector2.DistanceSquared(origin, target.Position) > 2.25f)
            return false;
        var group = worldStreamer.VisibleWorld.Sectors.SelectMany(s => s.IndoorTileGroups.Groups)
            .FirstOrDefault(g => g.Id == selected.Group && g.SurfaceLevel == surfaceLevel);
        if (group is null || IsArrival(group, selected.Trigger.WorldX, selected.Trigger.WorldY) ||
            FindDestination(group, selected.Trigger) is null) return false;
        _activated = selected;
        return true;
    }

    public void Reset()
    {
        _arrivalGroup = null;
        _arrivalTiles.Clear();
        _activated = null;
    }

    public bool Update(SacredCamera camera, IndoorTileGroup? active, out byte destinationLevel)
    {
        destinationLevel = active?.SurfaceLevel ?? 0;
        var activated = _activated;
        _activated = null;
        if (active is null) return false;
        var position = camera.WorldCenter;
        if (_arrivalGroup == active.Id)
        {
            if (_arrivalTiles.Any(tile => StairsMovementShortcut.IsTouchingTile(position, tile.X, tile.Y)))
                return false;
            Reset();
        }
        else if (_arrivalGroup is not null) Reset();

        var x = activated is { } selected && selected.Group == active.Id
            ? selected.Trigger.WorldX : (int)MathF.Floor(position.X);
        var y = activated is { } selectedY && selectedY.Group == active.Id
            ? selectedY.Trigger.WorldY : (int)MathF.Floor(position.Y);
        var source = active.Triggers.FirstOrDefault(t => t.WorldX == x && t.WorldY == y && t.IsStairs);
        if (!source.IsStairs || source.Pathing.IsBlocked) return false;
        var targetX = x + source.DestinationDeltaX;
        var targetY = y + source.DestinationDeltaY;
        var destination = FindDestination(active, source);
        if (destination is null) return false;

        // Keep the actor circle inside the destination cell when the authored shift
        // lands near a wall. Otherwise a transition could begin already penetrating it.
        var margin = WorldCollisionResolver.CharacterRadius + 0.002f;
        var fraction = activated is not null ? new Vector2(.5f) : position - new Vector2(x, y);
        var arrival = new Vector2(targetX + Math.Clamp(fraction.X, margin, 1 - margin),
            targetY + Math.Clamp(fraction.Y, margin, 1 - margin));
        _arrivalGroup = destination.Id;
        _arrivalTiles.Add((targetX, targetY));
        // Cover any connected arrival stair cells until the whole actor clears them.
        var pending = new Queue<(int X, int Y)>();
        pending.Enqueue((targetX, targetY));
        while (pending.TryDequeue(out var tile))
            foreach (var stair in destination.Triggers.Where(t => t.IsStairs &&
                Math.Abs(t.WorldX - tile.X) + Math.Abs(t.WorldY - tile.Y) <= 1))
                if (_arrivalTiles.Add((stair.WorldX, stair.WorldY))) pending.Enqueue((stair.WorldX, stair.WorldY));

        destinationLevel = destination.SurfaceLevel;
        camera.StopMoving();
        camera.CenterOnTile(arrival.X, arrival.Y);
        EngineLog.WriteLine($"Indoor stairs transition: {position.X:0.###},{position.Y:0.###} level {active.SurfaceLevel} -> " +
            $"{arrival.X:0.###},{arrival.Y:0.###} level {destinationLevel}; WLDX {source.Pathing.Properties} shift {source.DestinationDeltaX},{source.DestinationDeltaY}.");
        return true;
    }

    private bool IsArrival(IndoorTileGroup group, int x, int y) =>
        _arrivalGroup == group.Id && _arrivalTiles.Contains((x, y));

    private IndoorTileGroup? FindDestination(IndoorTileGroup active, IndoorTriggerTile source)
    {
        var level = active.SurfaceLevel + source.Pathing.Properties.SurfaceLevelDelta;
        return worldStreamer.VisibleWorld.Sectors.SelectMany(s => s.IndoorTileGroups.Groups)
            .FirstOrDefault(g => g.SurfaceLevel == level &&
                (active.BuildingAnchor is { } anchor ? g.BuildingAnchor == anchor :
                    g.WorldX == active.WorldX && g.WorldY == active.WorldY && g.Width == active.Width && g.Height == active.Height) &&
                g.TryGetLocalTile(source.WorldX + source.DestinationDeltaX, source.WorldY + source.DestinationDeltaY,
                    out var lx, out var ly) && !g.Pathing.IsBlocked(lx, ly));
    }
}
