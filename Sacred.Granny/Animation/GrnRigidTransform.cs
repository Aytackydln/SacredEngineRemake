using System.Numerics;

namespace Sacred.Granny.Animation;

internal static class GrnRigidTransform
{
    public static bool TryCreate(Matrix4x4 transform, out Matrix4x4 rigidTransform)
    {
        if (!Matrix4x4.Decompose(transform, out _, out var rotation, out var translation) ||
            !IsFinite(rotation) ||
            !IsFinite(translation) ||
            rotation.LengthSquared() <= 0.000001f)
        {
            rigidTransform = default;
            return false;
        }

        rigidTransform = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation));
        rigidTransform.Translation = translation;
        return true;
    }

    public static Matrix4x4 CreateOrOriginal(in Matrix4x4 transform) =>
        TryCreateFast(transform, out var fast) ? fast
        : TryCreate(transform, out var rigidTransform) ? rigidTransform : transform;

    /// <summary>Rotation + translation from a matrix whose 3x3 rows are mutually orthogonal with a positive
    /// determinant (any rotation, with uniform or per-axis scale): normalising the rows is exactly what
    /// Decompose would do, minus its expensive canonical-basis/determinant handling. Anything else
    /// (shear, mirroring, degenerate or non-affine input, NaN) returns false and takes the Decompose path.</summary>
    private static bool TryCreateFast(in Matrix4x4 m, out Matrix4x4 result)
    {
        const float OrthogonalTolerance = 1e-10f; // squared cosine between rows
        result = default;
        if (m.M14 != 0 || m.M24 != 0 || m.M34 != 0 || m.M44 != 1) return false;
        var x = new Vector3(m.M11, m.M12, m.M13);
        var y = new Vector3(m.M21, m.M22, m.M23);
        var z = new Vector3(m.M31, m.M32, m.M33);
        var xx = x.LengthSquared();
        var yy = y.LengthSquared();
        var zz = z.LengthSquared();
        // Comparisons are written so NaN fails them; the lower bound keeps clear of Decompose's degenerate-scale handling.
        if (!(xx > 1e-6f && yy > 1e-6f && zz > 1e-6f) || !float.IsFinite(xx + yy + zz)) return false;
        var xy = Vector3.Dot(x, y);
        var xz = Vector3.Dot(x, z);
        var yz = Vector3.Dot(y, z);
        if (!(xy * xy <= OrthogonalTolerance * xx * yy && xz * xz <= OrthogonalTolerance * xx * zz &&
              yz * yz <= OrthogonalTolerance * yy * zz)) return false;
        if (!(Vector3.Dot(Vector3.Cross(x, y), z) > 0)) return false;
        var translation = new Vector3(m.M41, m.M42, m.M43);
        if (!(float.IsFinite(translation.X) && float.IsFinite(translation.Y) && float.IsFinite(translation.Z))) return false;
        const float UnitTolerance = 1e-5f;
        if (MathF.Abs(xx - 1) < UnitTolerance && MathF.Abs(yy - 1) < UnitTolerance && MathF.Abs(zz - 1) < UnitTolerance)
        {
            // Already rigid (the common unscaled-bone case): Decompose would only rebuild the same matrix.
            result = m;
            return true;
        }
        x *= 1f / MathF.Sqrt(xx);
        y *= 1f / MathF.Sqrt(yy);
        z *= 1f / MathF.Sqrt(zz);
        result = new Matrix4x4(
            x.X, x.Y, x.Z, 0,
            y.X, y.Y, y.Z, 0,
            z.X, z.Y, z.Z, 0,
            translation.X, translation.Y, translation.Z, 1);
        return true;
    }

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
