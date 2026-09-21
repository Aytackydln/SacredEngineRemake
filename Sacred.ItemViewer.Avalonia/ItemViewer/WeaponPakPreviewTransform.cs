using System.Numerics;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

/// <summary>
/// Builds the rotation portion of Sacred's inventory-preview matrix.
/// Demo <c>TypeManager::getInventoryWorldMatrix</c> at <c>0x41F340</c> calls
/// the X, Y, and Z matrix helpers in record order and appends each to the
/// accumulated scale matrix. The native Z helper has the opposite sine signs
/// to System.Numerics; X and Y match. Matrices use row vectors.
/// </summary>
internal static class WeaponPakPreviewTransform
{
    public static Matrix4x4 CreateRotation(Vector3 rotation)
    {
        return Matrix4x4.CreateRotationX(rotation.X) *
               Matrix4x4.CreateRotationY(rotation.Y) *
               Matrix4x4.CreateRotationZ(-rotation.Z);
    }

    public static Matrix4x4 CreateWorld(float scale, Matrix4x4 rotation, Vector3 offset)
    {
        // A zero scale makes getInventoryWorldMatrix return false without writing
        // the caller's identity matrix. ty is stored but never applied here.
        return scale == 0.0f ? Matrix4x4.Identity :
            Matrix4x4.CreateScale(scale) * rotation *
            Matrix4x4.CreateTranslation(offset.X, 0.0f, offset.Z);
    }

    public static Matrix4x4 CreateViewProjection(float width, float height)
    {
        // renderMouse3D: right = +X, up = +Z, depth = -Y.
        var view = new Matrix4x4(
            1, 0, 0, 0,
            0, 0, -1, 0,
            0, 1, 0, 0,
            0, 0, 0, 1);
        return view * Matrix4x4.CreateOrthographicLeftHanded(width, height, -400, 2000);
    }
}
