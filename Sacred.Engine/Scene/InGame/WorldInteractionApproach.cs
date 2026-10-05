using System;
using System.Numerics;
using Sacred.World;

namespace Sacred.Engine.Scene.InGame;

internal static class WorldInteractionApproach
{
    public static Vector2 ClosestPoint(WorldFocusTarget target, Vector2 origin) => target.Model is { } model
        ? WorldObjectInteractionReach.ClosestPoint(model, origin) : target.Position;

    public static bool InReach(WorldFocusTarget target, Vector2 origin) => target.Model is { } model
        ? WorldObjectInteractionReach.Contains(model, origin)
        : Vector2.DistanceSquared(target.Position, origin) <= 1.5f * 1.5f;

    public static bool TryFind(WorldFocusTarget target, SacredCamera camera, WorldCollisionResolver collision,
        out Vector2 destination)
    {
        var origin = camera.WorldCenter;
        var point = ClosestPoint(target, origin);
        var offset = origin - point;
        var approach = point + (offset.LengthSquared() > 0 ? Vector2.Normalize(offset) : Vector2.Zero);
        var finder = new WorldClickPathFinder(collision);
        if (TryRoute(approach, out destination)) return true;
        // The nearest side can be blocked. Try standing positions around the authored footprint edge.
        for (var ring = 1; ring <= 3; ring++)
        for (var step = 0; step < 16; step++)
        {
            var angle = step * MathF.Tau / 16;
            var probe = point + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ring;
            var edge = ClosestPoint(target, probe);
            var outward = probe - edge;
            var candidate = edge + (outward.LengthSquared() > 0 ? Vector2.Normalize(outward) : Vector2.Zero);
            if (TryRoute(candidate, out destination)) return true;
        }
        destination = default;
        return false;

        bool TryRoute(Vector2 candidate, out Vector2 end)
        {
            end = default;
            if (!finder.TryFindRoute(origin, candidate, camera.Zoom, out var route) || route.Count == 0 ||
                !InReach(target, route[^1]) || !collision.CanOccupy(route[^1])) return false;
            end = route[^1];
            return true;
        }
    }
}
