using System.Numerics;

namespace Sacred.World.Map;

/// <summary>Gold cFontTTF2::printS circular text transform (0x6504E2..0x6506FE).
/// Label state 4 controls signed curvature; state 5 is the arc's angle in degrees.</summary>
public static class WorldMapLabelTransform
{
    public static Vector2 Transform(Vector2 textPosition, float textWidth, sbyte curvature, sbyte angleDegrees)
    {
        // Native labels use a 256-pixel layout rectangle and radius = width * 20 / |state4|.
        var radius = 256 * 20.0f / Math.Abs((int)curvature);
        var sign = Math.Sign(curvature);
        var angle = angleDegrees * (MathF.PI / 180) - textWidth * 0.005f + textPosition.X * 0.01f;
        var glyphRadius = radius + sign * textPosition.Y;
        return new(glyphRadius * MathF.Sin(angle), sign * (glyphRadius * MathF.Cos(angle) - radius));
    }
}
