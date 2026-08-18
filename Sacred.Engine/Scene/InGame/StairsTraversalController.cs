using System.Numerics;
using Sacred.Core.World.Stairs;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Applies linked stairs transitions and keeps the destination zone disarmed until it is left.</summary>
internal sealed class StairsTraversalController(SacredStairsMap stairsMap)
{
    private const float DestinationGraceMargin = 1.0f;

    private WorldStairsZone? _blockedDestinationZone;

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
        if (_blockedDestinationZone is { } blockedZone)
        {
            if (blockedZone.ContainsWithMargin(
                    actorPosition.X,
                    actorPosition.Y,
                    DestinationGraceMargin))
            {
                return false;
            }

            _blockedDestinationZone = null;
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
        _blockedDestinationZone = link.TargetZone;
        camera.StopMoving();
        camera.CenterOnTile(destination.X, destination.Y);
        return true;
    }
}
