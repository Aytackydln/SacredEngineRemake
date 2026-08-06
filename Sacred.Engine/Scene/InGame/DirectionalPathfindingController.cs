using System.Collections.Generic;
using System.Numerics;
using Sacred.World;

namespace Sacred.Engine.Scene.InGame;

/// <summary>
/// Converts a held analogue direction into short, reusable navigation routes. Planning a few
/// tiles ahead lets the player round authored obstacles without making stick movement feel like
/// a sequence of distant clicks.
/// </summary>
internal sealed class DirectionalPathfindingController
{
    private const float LookAheadDistance = 4.0f;
    private const float RepathDistance = 0.5f;
    private const float WaypointArrivalRadius = 0.03f;
    private const float DirectionChangeDotThreshold = 0.9848f; // Ten degrees.

    private readonly Queue<Vector2> _route = new();
    private Vector2 _plannedDirection;
    private Vector2 _plannedFrom;

    public bool TryGetWaypoint(
        Vector2 position,
        Vector2 direction,
        WorldCollisionResolver collision,
        out Vector2 waypoint)
    {
        var normalizedDirection = Vector2.Normalize(direction);
        DiscardReachedWaypoints(position);

        var movedSincePlan = Vector2.DistanceSquared(position, _plannedFrom) >= RepathDistance * RepathDistance;
        var directionChanged = Vector2.Dot(normalizedDirection, _plannedDirection) < DirectionChangeDotThreshold;
        if (_route.Count == 0 || movedSincePlan || directionChanged)
            RebuildRoute(position, normalizedDirection, collision);

        return _route.TryPeek(out waypoint);
    }

    public void Reset()
    {
        _route.Clear();
        _plannedDirection = Vector2.Zero;
        _plannedFrom = Vector2.Zero;
    }

    private void RebuildRoute(
        Vector2 position,
        Vector2 direction,
        WorldCollisionResolver collision)
    {
        _route.Clear();
        _plannedDirection = direction;
        _plannedFrom = position;

        var target = position + direction * LookAheadDistance;
        var pathFinder = new WorldClickPathFinder(collision);
        if (!pathFinder.TryFindRoute(position, target, 1.0f, out var route))
            return;

        foreach (var routePoint in route)
            _route.Enqueue(routePoint);
        DiscardReachedWaypoints(position);
    }

    private void DiscardReachedWaypoints(Vector2 position)
    {
        while (_route.TryPeek(out var waypoint) &&
               Vector2.DistanceSquared(position, waypoint) <= WaypointArrivalRadius * WaypointArrivalRadius)
        {
            _route.Dequeue();
        }
    }
}
