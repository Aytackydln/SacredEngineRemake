using System.Numerics;
using Sacred.Core.World.Stairs;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Applies linked stairs transitions and keeps arrival stairs disarmed until fully cleared.</summary>
internal sealed class StairsTraversalController(SacredStairsMap stairsMap)
{
    private StairsArrivalGuard? _arrivalGuard;
    private WorldStairsLink? _movementLink;

    public void ObserveMovement(Vector2 start, Vector2 end, byte surfaceLevel)
    {
        if (_movementLink is not null)
            return;

        if (StairsMovementShortcut.TryFindLink(stairsMap, start, end, surfaceLevel,
                position => _arrivalGuard?.BlocksTile(position, surfaceLevel) == true, out var link))
            _movementLink = link;
    }

    public bool IsStairsAt(Vector2 worldPosition, byte surfaceLevel) =>
        stairsMap.TryGetLink(
            worldPosition.X,
            worldPosition.Y,
            surfaceLevel,
            out _);

    public bool Update(SacredCamera camera, byte surfaceLevel, out byte destinationSurfaceLevel)
    {
        destinationSurfaceLevel = surfaceLevel;
        var movementLink = _movementLink;
        _movementLink = null;
        var actorPosition = camera.WorldCenter;
        if (_arrivalGuard is { } arrivalGuard && !arrivalGuard.IsTouching(actorPosition, surfaceLevel))
            _arrivalGuard = null;

        if (movementLink is null && _arrivalGuard?.BlocksTile(actorPosition, surfaceLevel) == true)
            return false;

        if (movementLink is null && !stairsMap.TryGetLink(
                actorPosition.X,
                actorPosition.Y,
                surfaceLevel,
                out movementLink))
        {
            return false;
        }

        var destination = movementLink.Destination;
        destinationSurfaceLevel = movementLink.TargetZone.Anchor.Metadata;
        _arrivalGuard = new StairsArrivalGuard(movementLink);
        camera.StopMoving();
        camera.CenterOnTile(destination.X, destination.Y);
        EngineLog.WriteLine($"Stairs transition: {actorPosition.X:0.##},{actorPosition.Y:0.##} level {surfaceLevel} -> {destination.X:0.##},{destination.Y:0.##} level {destinationSurfaceLevel}");
        return true;
    }

}
