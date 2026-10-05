using System.Numerics;

namespace Sacred.World;

/// <summary>
/// Finds routes through the streamed WLDX grid and live door patches. Detour bounds
/// scale with both click distance and zoom, with one bounded search for target fallbacks.
/// </summary>
public sealed class WorldClickPathFinder(WorldCollisionResolver collision)
{
    private const int MaximumSearchNodes = 48_000;
    private const int TargetSearchRadius = 8;
    private const int MaximumTargetCandidates = 16;
    private const int TargetPositionRefinementIterations = 12;
    private const float TargetContactPadding = 0.002f;

    private static readonly TileOffset[] Neighbours =
    [
        new(-1, -1), new(0, -1), new(1, -1),
        new(-1, 0),               new(1, 0),
        new(-1, 1),  new(0, 1),  new(1, 1)
    ];

    /// <summary>
    /// Returns a simplified route from the current location to the clicked location.
    /// A click on a blocker ends at the closest reachable navigation cell instead.
    /// </summary>
    public bool TryFindRoute(Vector2 start, Vector2 target, float zoom, out IReadOnlyList<Vector2> route)
    {
        if (collision.CanMoveDirectly(start, target))
        {
            route = [target];
            return true;
        }

        var bounds = SearchBounds.Create(start, target, zoom);
        var startTile = TileCoordinate.FromWorld(start);
        var targetCandidates = FindTraversableTargetTiles(TileCoordinate.FromWorld(target), target, bounds);
        if (targetCandidates.Count == 0)
        {
            route = Array.Empty<Vector2>();
            return false;
        }

        if (!bounds.Contains(startTile))
        {
            route = Array.Empty<Vector2>();
            return false;
        }

        if (!TryFindTileRoute(start, startTile, targetCandidates, target, bounds,
                out var tiles, out var selectedTarget))
        {
            route = Array.Empty<Vector2>();
            return false;
        }

        route = Simplify(start, selectedTarget, tiles);
        return route.Count > 0;
    }

    private List<TargetCandidate> FindTraversableTargetTiles(
        TileCoordinate targetTile,
        Vector2 requestedTarget,
        SearchBounds bounds)
    {
        var candidates = new List<TargetCandidate>();
        for (var radius = 0; radius <= TargetSearchRadius; radius++)
        {
            for (var y = -radius; y <= radius; y++)
            for (var x = -radius; x <= radius; x++)
            {
                if (Math.Max(Math.Abs(x), Math.Abs(y)) != radius)
                    continue;

                var candidate = new TileCoordinate(targetTile.X + x, targetTile.Y + y);
                var center = candidate.ToWorld();
                if (bounds.Contains(candidate) &&
                    IsTraversable(candidate) &&
                    collision.CanOccupy(center))
                {
                    var position = ClosestOccupiablePoint(candidate, requestedTarget, center);
                    candidates.Add(new TargetCandidate(
                        candidate,
                        position,
                        Vector2.DistanceSquared(position, requestedTarget)));
                }
            }
        }

        candidates.Sort(static (left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));
        return candidates;
    }

    /// <summary>
    /// Keeps a blocked click as close as possible to the requested point. Falling back to the
    /// cell centre caused a visible half-tile horizontal jump on narrow walkways such as bridges.
    /// </summary>
    private Vector2 ClosestOccupiablePoint(
        TileCoordinate tile,
        Vector2 requestedTarget,
        Vector2 knownOccupiableCenter)
    {
        var inset = WorldCollisionResolver.CharacterRadius + TargetContactPadding;
        var desired = Vector2.Clamp(
            requestedTarget,
            new Vector2(tile.X + inset, tile.Y + inset),
            new Vector2(tile.X + 1.0f - inset, tile.Y + 1.0f - inset));
        if (collision.CanOccupy(desired))
            return desired;

        var occupiable = knownOccupiableCenter;
        var blocked = desired;
        for (var iteration = 0; iteration < TargetPositionRefinementIterations; iteration++)
        {
            var middle = (occupiable + blocked) * 0.5f;
            if (collision.CanOccupy(middle))
                occupiable = middle;
            else
                blocked = middle;
        }

        return occupiable;
    }

    private bool TryFindTileRoute(
        Vector2 start,
        TileCoordinate startTile,
        IReadOnlyList<TargetCandidate> candidates,
        Vector2 target,
        SearchBounds bounds,
        out List<TileCoordinate> route,
        out Vector2 selectedTarget)
    {
        var open = new PriorityQueue<TileCoordinate, float>();
        var cameFrom = new Dictionary<TileCoordinate, TileCoordinate>();
        var costs = new Dictionary<TileCoordinate, float> { [startTile] = 0.0f };
        open.Enqueue(startTile, EstimateCost(startTile, target));
        var targets = candidates.Take(MaximumTargetCandidates).Select((candidate, rank) => (candidate, rank))
            .ToDictionary(pair => pair.candidate.Tile);
        var bestRank = int.MaxValue;
        TargetCandidate? best = null;
        var traversable = new Dictionary<TileCoordinate, bool>();
        var closed = new HashSet<TileCoordinate>();

        var inspected = 0;
        while (open.TryDequeue(out var current, out _))
        {
            if (!closed.Add(current))
                continue;

            if (++inspected > MaximumSearchNodes)
                break;

            if (targets.TryGetValue(current, out var goal) && goal.rank < bestRank)
            {
                best = goal.candidate;
                bestRank = goal.rank;
                // The nearest occupiable target has been reached. Otherwise keep searching
                // for a closer candidate without restarting A* for each unreachable cell.
                if (bestRank == 0) break;
            }

            var currentWorld = current.ToWorld();
            foreach (var offset in Neighbours)
            {
                var next = new TileCoordinate(current.X + offset.X, current.Y + offset.Y);
                if (!bounds.Contains(next) || closed.Contains(next) || !CanTraverse(next))
                    continue;

                var nextWorld = next.ToWorld();
                var edgeStart = current == startTile ? start : currentWorld;
                if (!collision.CanMoveDirectly(edgeStart, nextWorld))
                    continue;

                var nextCost = costs[current] + Vector2.Distance(edgeStart, nextWorld);
                if (costs.TryGetValue(next, out var knownCost) && knownCost <= nextCost)
                    continue;

                costs[next] = nextCost;
                cameFrom[next] = current;
                open.Enqueue(next, nextCost + EstimateCost(next, target));
            }
        }

        if (best is { } found)
        {
            route = ReconstructRoute(cameFrom, found.Tile);
            selectedTarget = found.Position;
            return true;
        }
        route = [];
        selectedTarget = target;
        return false;

        bool CanTraverse(TileCoordinate tile)
        {
            if (traversable.TryGetValue(tile, out var allowed)) return allowed;
            allowed = IsTraversable(tile) && collision.CanOccupy(tile.ToWorld());
            traversable[tile] = allowed;
            return allowed;
        }
    }

    private bool IsTraversable(TileCoordinate tile) =>
        !collision.IsMovementBlocked(tile.X, tile.Y);

    private IReadOnlyList<Vector2> Simplify(
        Vector2 start,
        Vector2 requestedTarget,
        IReadOnlyList<TileCoordinate> tileRoute)
    {
        var points = new List<Vector2>(tileRoute.Count + 2) { start };
        for (var index = 1; index < tileRoute.Count; index++)
        {
            var tile = tileRoute[index];
            points.Add(tile.ToWorld());
        }

        var finalPoint = collision.CanMoveDirectly(points[^1], requestedTarget)
            ? requestedTarget
            : points[^1];
        if (Vector2.DistanceSquared(points[^1], finalPoint) > float.Epsilon)
            points.Add(finalPoint);

        if (points.Count == 1)
            return Array.Empty<Vector2>();

        var simplified = new List<Vector2>(points.Count);
        var from = 0;
        while (from < points.Count - 1)
        {
            var to = points.Count - 1;
            while (to > from + 1 && !collision.CanMoveDirectly(points[from], points[to]))
                to--;

            simplified.Add(points[to]);
            from = to;
        }

        return simplified;
    }

    private static List<TileCoordinate> ReconstructRoute(
        IReadOnlyDictionary<TileCoordinate, TileCoordinate> cameFrom,
        TileCoordinate current)
    {
        var route = new List<TileCoordinate> { current };
        while (cameFrom.TryGetValue(current, out var previous))
        {
            route.Add(previous);
            current = previous;
        }

        route.Reverse();
        return route;
    }

    private static float EstimateCost(TileCoordinate from, Vector2 requestedTarget) =>
        Vector2.Distance(from.ToWorld(), requestedTarget);

    private readonly record struct TileOffset(int X, int Y);

    private readonly record struct TargetCandidate(
        TileCoordinate Tile,
        Vector2 Position,
        float DistanceSquared);

    private readonly record struct TileCoordinate(int X, int Y)
    {
        public static TileCoordinate FromWorld(Vector2 world) =>
            new((int)MathF.Floor(world.X), (int)MathF.Floor(world.Y));

        public Vector2 ToWorld() => new(X + 0.5f, Y + 0.5f);
    }

    private readonly record struct SearchBounds(int MinimumX, int MaximumX, int MinimumY, int MaximumY)
    {
        public static SearchBounds Create(Vector2 start, Vector2 target, float zoom)
        {
            var safeZoom = Math.Max(zoom, 0.25f);
            var distance = Vector2.Distance(start, target);
            var detourPadding = Math.Clamp((int)MathF.Ceiling(MathF.Max(24.0f / safeZoom, distance * 0.5f)), 24, 96);
            return new SearchBounds(
                (int)MathF.Floor(MathF.Min(start.X, target.X)) - detourPadding,
                (int)MathF.Ceiling(MathF.Max(start.X, target.X)) + detourPadding,
                (int)MathF.Floor(MathF.Min(start.Y, target.Y)) - detourPadding,
                (int)MathF.Ceiling(MathF.Max(start.Y, target.Y)) + detourPadding);
        }

        public bool Contains(TileCoordinate tile) =>
            tile.X >= MinimumX && tile.X <= MaximumX &&
            tile.Y >= MinimumY && tile.Y <= MaximumY;
    }
}
