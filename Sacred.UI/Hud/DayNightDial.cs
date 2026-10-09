using System;
using System.Numerics;

namespace Sacred.UI.Hud;

/// <summary>The native calendar rotation, followed by its horizontal factor of two.</summary>
public static class DayNightDial
{
    public static Vector2 Transform(Vector2 corner, float dayFraction)
    {
        var angle = dayFraction * MathF.Tau;
        var cosine = MathF.Cos(angle);
        var sine = MathF.Sin(angle);
        return BottomHudLayout.DialCenter + new Vector2(
            2 * (cosine * corner.X - sine * corner.Y), sine * corner.X + cosine * corner.Y);
    }
}
