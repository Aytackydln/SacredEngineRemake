using System.Numerics;
using Sacred.World.Geometry;

namespace Sacred.World.Networking;

/// <summary>Converts Gold projected-world headings to model/tile directions; packet words remain unchanged.</summary>
public static class SacredNetworkFacing
{
    public static float ToModelRadians(uint bits)
    {
        return CreatureModelPose.RotationFromProjectedHeading(BitConverter.UInt32BitsToSingle(bits));
    }
    public static Vector2 ToTileDirection(uint bits)
    {
        // Invert the wire projection directly; model-camera scaling is a separate step.
        var angle = BitConverter.UInt32BitsToSingle(bits) * (MathF.PI / 180);
        return Vector2.Normalize(new(MathF.Cos(angle) + 2 * MathF.Sin(angle),
            2 * MathF.Sin(angle) - MathF.Cos(angle)));
    }
    public static uint FromTileDirection(Vector2 direction)
    {
        if (direction == Vector2.Zero) throw new ArgumentException("Facing requires a direction.", nameof(direction));
        var degrees = MathF.Atan2(direction.X + direction.Y, 2 * (direction.X - direction.Y)) * 180 / MathF.PI;
        return BitConverter.SingleToUInt32Bits(degrees);
    }
}
