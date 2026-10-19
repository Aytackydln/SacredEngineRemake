using System.Numerics;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.World;

/// <summary>Gold 0x44E814..0x44EA28 and Granny's matrix-to-normalized-quaternion setter.</summary>
public static class GameplayDroppedItemOrientation
{
    /// <summary>Gold 0x44DBF0: physical world direction projected into the native model camera.
    /// This is distinct from an authored Items.pak angle and from a projected-world packet heading.</summary>
    public static Matrix4x4 SettledMatrix(GoldPhysicalVector direction)
    {
        var x = (direction.X - direction.Y) * (2 / MathF.Sqrt(5)) * (534f / 1024);
        var y = (direction.X + direction.Y) * (400f / 768);
        return float.IsFinite(x) && float.IsFinite(y) && (x != 0 || y != 0)
            ? Matrix4x4.CreateRotationZ(MathF.Atan2(y, x)) : Matrix4x4.Identity;
    }

    public static Matrix4x4 NativeMatrix(Vector3 angles)
    {
        var cx = MathF.Cos(angles.X); var sx = MathF.Sin(angles.X);
        var cy = MathF.Cos(angles.Y); var sy = MathF.Sin(angles.Y);
        var cz = MathF.Cos(angles.Z); var sz = MathF.Sin(angles.Z);
        var z = new Matrix4x4(cz, sz, 0, 0, -sz, cz, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
        var y = new Matrix4x4(cy, 0, sy, 0, 0, 1, 0, 0, -sy, 0, cy, 0, 0, 0, 0, 1);
        // Preserve the original stores at 0x44E9C3/0x44E9C7, including cos(X) in row 1, column 0.
        // The setter converts this basis to a quaternion and normalizes it; treating it as Euler angles differs.
        var x = new Matrix4x4(1, 0, 0, 0, cx, 0, sx, 0, -sx, 0, cx, 0, 0, 0, 0, 1);
        return z * y * x;
    }

    // Gold sets Granny's context +0x240 matrix convention to 1: the setter transposes
    // its input before its column-vector quaternion conversion. System.Numerics uses row vectors.
    public static Matrix4x4 ModelMatrix(Vector3 angles) => Matrix4x4.CreateFromQuaternion(
        Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(NativeMatrix(angles))));
}
