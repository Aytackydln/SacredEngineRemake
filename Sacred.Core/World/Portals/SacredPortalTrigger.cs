using System.Numerics;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Core.World.Portals;

/// <summary>Inclusive script trigger rectangle and its unconditional Teleport callback.</summary>
public sealed record SacredPortalTrigger(string Name, int MinimumX, int MinimumY, int MaximumX,
    int MaximumY, byte SurfaceLevel, SacredScriptPosition Destination)
{
    public bool Contains(Vector2 position, byte surfaceLevel) => surfaceLevel == SurfaceLevel &&
        position.X >= MinimumX && position.X < MaximumX + 1 &&
        position.Y >= MinimumY && position.Y < MaximumY + 1;

    /// <summary>Tests the entire movement segment so a fast step cannot skip a one-cell trigger.</summary>
    public bool Intersects(Vector2 start, Vector2 end, byte surfaceLevel)
    {
        if (surfaceLevel != SurfaceLevel) return false;
        var delta = end - start;
        var enter = 0f;
        var exit = 1f;
        if (!Clip(start.X, delta.X, MinimumX, MaximumX + 1, ref enter, ref exit) ||
            !Clip(start.Y, delta.Y, MinimumY, MaximumY + 1, ref enter, ref exit)) return false;
        return enter < exit || Contains(start + delta * enter, surfaceLevel);
    }

    private static bool Clip(float start, float delta, float minimum, float maximum, ref float enter, ref float exit)
    {
        if (delta == 0) return start >= minimum && start < maximum;
        var first = (minimum - start) / delta;
        var last = (maximum - start) / delta;
        if (first > last) (first, last) = (last, first);
        enter = MathF.Max(enter, first);
        exit = MathF.Min(exit, last);
        return enter <= exit;
    }
}
