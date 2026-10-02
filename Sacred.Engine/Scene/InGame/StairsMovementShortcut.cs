using System;
using System.Numerics;
using Sacred.Core.World.Stairs;
using Sacred.World;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Finds the first linked stairs tile touched by a travelled collision segment.</summary>
internal static class StairsMovementShortcut
{
    // Include the collision resolver's contact skin so a blocked stairs tile is reachable.
    private const float Radius = WorldCollisionResolver.CharacterRadius + 0.002f;

    public static bool IsTouchingTile(Vector2 position, int x, int y)
    {
        var closest = new Vector2(Math.Clamp(position.X, x, x + 1.0f), Math.Clamp(position.Y, y, y + 1.0f));
        return Vector2.DistanceSquared(position, closest) <= Radius * Radius;
    }

    public static bool TryFindLink(
        SacredStairsMap stairs,
        Vector2 start,
        Vector2 end,
        byte surfaceLevel,
        Func<Vector2, bool> isDisarmed,
        out WorldStairsLink link)
    {
        link = null!;
        var delta = end - start;
        if (delta.LengthSquared() <= float.Epsilon)
            return false;

        var minimumX = (int)MathF.Floor(MathF.Min(start.X, end.X) - Radius);
        var maximumX = (int)MathF.Floor(MathF.Max(start.X, end.X) + Radius);
        var minimumY = (int)MathF.Floor(MathF.Min(start.Y, end.Y) - Radius);
        var maximumY = (int)MathF.Floor(MathF.Max(start.Y, end.Y) + Radius);
        var firstTime = float.PositiveInfinity;
        for (var y = minimumY; y <= maximumY; y++)
        for (var x = minimumX; x <= maximumX; x++)
        {
            var center = new Vector2(x + 0.5f, y + 0.5f);
            if (isDisarmed(center) || !stairs.TryGetLink(center.X, center.Y, surfaceLevel, out var candidate))
                continue;

            var minimum = new Vector2(x, y);
            var maximum = minimum + Vector2.One;
            // A circle swept against a tile is the union of two face rectangles and four corners.
            var time = MathF.Min(
                BoxEntry(start, delta, minimum - new Vector2(Radius, 0), maximum + new Vector2(Radius, 0)),
                BoxEntry(start, delta, minimum - new Vector2(0, Radius), maximum + new Vector2(0, Radius)));
            time = MathF.Min(time, CircleEntry(start, delta, minimum));
            time = MathF.Min(time, CircleEntry(start, delta, maximum));
            time = MathF.Min(time, CircleEntry(start, delta, new Vector2(minimum.X, maximum.Y)));
            time = MathF.Min(time, CircleEntry(start, delta, new Vector2(maximum.X, minimum.Y)));
            if (time < firstTime)
            {
                firstTime = time;
                link = candidate;
            }
        }

        return link is not null;
    }

    private static float BoxEntry(Vector2 start, Vector2 delta, Vector2 minimum, Vector2 maximum)
    {
        var entry = 0.0f;
        var exit = 1.0f;
        for (var axis = 0; axis < 2; axis++)
        {
            var position = axis == 0 ? start.X : start.Y;
            var movement = axis == 0 ? delta.X : delta.Y;
            var lower = axis == 0 ? minimum.X : minimum.Y;
            var upper = axis == 0 ? maximum.X : maximum.Y;
            if (MathF.Abs(movement) <= float.Epsilon)
            {
                if (position < lower || position > upper)
                    return float.PositiveInfinity;
                continue;
            }

            var first = (lower - position) / movement;
            var last = (upper - position) / movement;
            entry = MathF.Max(entry, MathF.Min(first, last));
            exit = MathF.Min(exit, MathF.Max(first, last));
            if (entry > exit)
                return float.PositiveInfinity;
        }

        return entry;
    }

    private static float CircleEntry(Vector2 start, Vector2 delta, Vector2 center)
    {
        var offset = start - center;
        var c = offset.LengthSquared() - Radius * Radius;
        if (c <= 0)
            return 0;
        var a = delta.LengthSquared();
        var b = Vector2.Dot(offset, delta);
        var discriminant = b * b - a * c;
        if (discriminant < 0)
            return float.PositiveInfinity;
        var time = (-b - MathF.Sqrt(discriminant)) / a;
        return time is >= 0 and <= 1 ? time : float.PositiveInfinity;
    }
}
