using System.Numerics;

namespace Sacred.Core.World;

/// <summary>Ranks candidates by both proximity and alignment with the intended movement.</summary>
public static class WorldFocusCone
{
    public static bool TryScore(Vector2 origin, Vector2 direction, Vector2 target,
        float range, float halfAngleDegrees, out float score)
    {
        score = float.PositiveInfinity;
        if (!float.IsFinite(range) || range <= 0 || !float.IsFinite(halfAngleDegrees) ||
            halfAngleDegrees <= 0 || halfAngleDegrees >= 90 ||
            !float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || direction.LengthSquared() < 0.000001f)
            return false;
        var offset = target - origin;
        var distance = offset.Length();
        if (!float.IsFinite(distance) || distance > range) return false;
        var cosine = distance <= 0.0001f ? 1 : Vector2.Dot(Vector2.Normalize(direction), offset / distance);
        var limit = MathF.Cos(halfAngleDegrees * MathF.PI / 180);
        if (cosine < limit) return false;
        // Equal weighting: moving to the cone edge costs the same as moving to its range limit.
        score = distance / range + (1 - cosine) / (1 - limit);
        return true;
    }
}
