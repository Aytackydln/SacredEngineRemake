using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.World.Stairs;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Keeps arrival stairs disarmed until the actor fully clears their trigger area.</summary>
internal sealed class StairsArrivalGuard
{
    private readonly byte _surfaceLevel;
    private readonly HashSet<(int X, int Y)> _tiles = [];

    public StairsArrivalGuard(WorldStairsLink link)
    {
        _surfaceLevel = link.TargetZone.Anchor.Metadata;
        foreach (var cell in link.TargetZone.Cells)
            _tiles.Add((cell.Position.X, cell.Position.Y));

        // DefPos arrival markers may lie outside the linked stairs zone.
        _tiles.Add(((int)MathF.Floor(link.Destination.X), (int)MathF.Floor(link.Destination.Y)));
    }

    public bool BlocksTile(Vector2 position, byte surfaceLevel) =>
        surfaceLevel == _surfaceLevel &&
        _tiles.Contains(((int)MathF.Floor(position.X), (int)MathF.Floor(position.Y)));

    public bool IsTouching(Vector2 position, byte surfaceLevel)
    {
        if (surfaceLevel != _surfaceLevel)
            return false;

        foreach (var tile in _tiles)
        {
            if (StairsMovementShortcut.IsTouchingTile(position, tile.X, tile.Y))
                return true;
        }

        return false;
    }
}
