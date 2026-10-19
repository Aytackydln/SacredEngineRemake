using System.Numerics;

namespace Sacred.GameplayHost.World;

/// <summary>Gold 0x44D970 aligns the model's +Y axis with the flight vector,
/// using axis (direction.Z, 0, -direction.X) and angle acos(direction.Y).</summary>
public static class GameplayArrowOrientation
{
    public static Matrix4x4 FromDirection(Vector3 direction)
    {
        if (direction.LengthSquared() < 1e-14f) return Matrix4x4.Identity;
        direction = Vector3.Normalize(direction);
        var axis = Vector3.Cross(Vector3.UnitY, direction);
        if (axis.LengthSquared() < 1e-14f)
            return direction.Y < 0 ? Matrix4x4.CreateRotationZ(MathF.PI) : Matrix4x4.Identity;
        return Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(direction.Y, -1, 1)));
    }
}
