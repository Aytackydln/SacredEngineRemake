using System;
using System.Numerics;
using Sacred.Core.World.Stairs;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Applies linked stairs transitions and keeps the arrival tile disarmed until it is left.</summary>
internal sealed class StairsTraversalController(SacredStairsMap stairsMap)
{
    private StairsArrivalTile? _blockedArrivalTile;

    public bool IsStairsAt(Vector2 worldPosition, byte surfaceLevel) =>
        stairsMap.TryGetLink(
            worldPosition.X,
            worldPosition.Y,
            surfaceLevel,
            out _);

    public bool Update(SacredCamera camera, byte surfaceLevel, out byte destinationSurfaceLevel)
    {
        destinationSurfaceLevel = surfaceLevel;
        var actorPosition = camera.WorldCenter;
        if (_blockedArrivalTile is { } blockedArrivalTile)
        {
            if (blockedArrivalTile.Contains(actorPosition, surfaceLevel))
            {
                return false;
            }

            _blockedArrivalTile = null;
        }

        if (!stairsMap.TryGetLink(
                actorPosition.X,
                actorPosition.Y,
                surfaceLevel,
                out var link))
        {
            return false;
        }

        var destination = link.Destination;
        destinationSurfaceLevel = link.TargetZone.Anchor.Metadata;
        _blockedArrivalTile = StairsArrivalTile.From(destination, destinationSurfaceLevel);
        camera.StopMoving();
        camera.CenterOnTile(destination.X, destination.Y);
        return true;
    }

    private readonly record struct StairsArrivalTile(int X, int Y, byte SurfaceLevel)
    {
        public static StairsArrivalTile From(WorldStairsDestination destination, byte surfaceLevel) =>
            new((int)MathF.Floor(destination.X), (int)MathF.Floor(destination.Y), surfaceLevel);

        public bool Contains(Vector2 worldPosition, byte surfaceLevel) =>
            SurfaceLevel == surfaceLevel &&
            X == (int)MathF.Floor(worldPosition.X) &&
            Y == (int)MathF.Floor(worldPosition.Y);
    }
}
