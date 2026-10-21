using System.Numerics;

namespace Sacred.World.Geometry;

/// <summary>Gold setRelFacing (0x44DBF0): logical directions pass through the
/// native model camera before setting the Granny sequence orientation.</summary>
public static class CreatureModelPose
{
    private static readonly float NativeXScale = (2 / MathF.Sqrt(5)) * (534f / 1024);
    private const float NativeYScale = 400f / 768;

    public static float RotationRadians(Vector2 direction) =>
        -MathF.Atan2((direction.X + direction.Y) * NativeYScale,
            (direction.X - direction.Y) * NativeXScale);

    /// <summary>Stop packets carry a projected-world angle, whose X/Y ratio is 2:1.</summary>
    public static float RotationFromProjectedHeading(float degrees)
    {
        var angle = degrees * (MathF.PI / 180);
        return MathF.Atan2(-2 * NativeYScale * MathF.Sin(angle), NativeXScale * MathF.Cos(angle));
    }
}
