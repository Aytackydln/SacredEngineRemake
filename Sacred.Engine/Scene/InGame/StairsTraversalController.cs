using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.World.Stairs;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Applies linked stairs transitions and keeps arrival stairs disarmed until fully cleared.</summary>
internal sealed class StairsTraversalController(SacredStairsMap stairsMap)
{
    private StairsArrivalGuard? _arrivalGuard;
    private WorldStairsLink? _movementLink;

    public IEnumerable<WorldFocusTarget> FocusTargets(Vector2 origin, byte surfaceLevel, float range)
    {
        var minimumX = (int)MathF.Floor(origin.X - range);
        var maximumX = (int)MathF.Floor(origin.X + range);
        var minimumY = (int)MathF.Floor(origin.Y - range);
        var maximumY = (int)MathF.Floor(origin.Y + range);
        for (var y = minimumY; y <= maximumY; y++)
        for (var x = minimumX; x <= maximumX; x++)
        {
            var center = new Vector2(x + 0.5f, y + 0.5f);
            if (_arrivalGuard?.BlocksTile(center, surfaceLevel) != true &&
                stairsMap.TryGetLink(center.X, center.Y, surfaceLevel, out var link))
                yield return new WorldFocusTarget(link.SourceZone.Anchor.ToPacked(), center, null, link);
        }
    }

    public bool TryActivate(WorldFocusTarget target, Vector2 origin, byte surfaceLevel)
    {
        if (target.Stairs is not { } link || link.SourceZone.Anchor.Metadata != surfaceLevel ||
            Vector2.DistanceSquared(origin, target.Position) > 2.25f ||
            _arrivalGuard?.BlocksTile(target.Position, surfaceLevel) == true) return false;
        _movementLink = link;
        return true;
    }

    public void Reset(Vector2 position, byte surfaceLevel)
    {
        _movementLink = null;
        _arrivalGuard = stairsMap.TryGetZone(position.X, position.Y, surfaceLevel, out var zone)
            ? new StairsArrivalGuard(zone, position) : null;
    }

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
