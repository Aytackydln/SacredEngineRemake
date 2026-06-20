using System.Numerics;

namespace Sacred.Granny;

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

    public static Matrix4x4 CreateOrOriginal(Matrix4x4 transform) =>
        TryCreate(transform, out var rigidTransform) ? rigidTransform : transform;

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
