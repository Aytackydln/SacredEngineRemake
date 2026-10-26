using System;
using System.Numerics;

namespace AssetViewer.ItemViewer;

/// <summary>The asset camera convention shared by DX12 and WebGPU.</summary>
internal static class PreviewCamera
{
    private const float FieldOfView = MathF.PI / 4;
    internal static float Diameter(Vector3 min, Vector3 max) => Math.Max(1, Vector3.Distance(min, max));
    internal static Matrix4x4 World(Vector3 min, Vector3 max, float yaw, float pitch, float roll) =>
        Matrix4x4.CreateTranslation(-(min + max) * .5f) *
        Matrix4x4.CreateRotationZ(yaw) * Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationY(roll);

    internal static Vector3 Position(float diameter, float aspect, float zoom, bool ground)
    {
        if (!ground) return new Vector3(0, -diameter * 2, diameter * .65f);
        var halfAngle = MathF.Atan(MathF.Tan(FieldOfView * .5f) * Math.Min(1, aspect));
        return Vector3.Normalize(new Vector3(0, -2, .65f)) * (diameter * .5f / MathF.Sin(halfAngle) * 1.15f / zoom);
    }

    internal static Matrix4x4 ViewProjection(float diameter, float aspect, float zoom, bool ground)
    {
        var camera = Position(diameter, aspect, zoom, ground);
        var view = Matrix4x4.CreateLookAt(camera, Vector3.Zero, Vector3.UnitZ);
        if (ground) return view * Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect,
            Math.Max(.001f, diameter * .001f), camera.Length() + diameter * 4);
        var height = diameter * Math.Max(1, 1 / aspect) * 1.15f / zoom;
        return view * Matrix4x4.CreateOrthographic(height * aspect, height, .01f, diameter * 5);
    }
}
