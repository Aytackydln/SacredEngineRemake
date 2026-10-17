using System.Numerics;

namespace Sacred.World.Networking;

/// <summary>Converts Gold projected-world headings to model/tile directions; packet words remain unchanged.</summary>
public static class SacredNetworkFacing
{
    public static float ToModelRadians(uint bits)
    {
        var angle = BitConverter.UInt32BitsToSingle(bits) * (MathF.PI / 180);
        // The native physical projection has a 2:1 XY ratio. Authored artwork
        // angles use a different camera transform (WorldModelPose).
        return MathF.Atan2(-2 * MathF.Sin(angle), MathF.Cos(angle));
    }
    public static Vector2 ToTileDirection(uint bits)
    {
        var angle = -ToModelRadians(bits) - MathF.PI / 4;
        return new(MathF.Cos(angle), MathF.Sin(angle));
    }
    public static uint FromTileDirection(Vector2 direction)
    {
        if (direction == Vector2.Zero) throw new ArgumentException("Facing requires a direction.", nameof(direction));
        var degrees = MathF.Atan2(direction.X + direction.Y, 2 * (direction.X - direction.Y)) * 180 / MathF.PI;
        return BitConverter.SingleToUInt32Bits(degrees);
    }
}
