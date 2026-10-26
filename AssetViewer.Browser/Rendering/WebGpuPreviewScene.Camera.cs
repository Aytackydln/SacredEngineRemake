using System.Numerics;
using System.Runtime.InteropServices;
using AssetViewer.ItemViewer;
using Sacred.Engine.Scene.InGame;

namespace AssetViewer.Browser.Rendering;

internal sealed partial class WebGpuPreviewScene
{
    private Vector3 FrameMin => _frame?.Min ?? _fitMin;
    private Vector3 FrameMax => _frame?.Max ?? _fitMax;
    private float Diameter => PreviewCamera.Diameter(FrameMin, FrameMax);
    private Vector2 InventoryCenter => new(-64 + (4 - _gridWidth) / 2 * 32 + _gridWidth * 16,
        -80 + (5 - _gridHeight) / 2 * 32 + _gridHeight * 16);

    private Matrix4x4 World()
    {
        if (AssetPreview) return PreviewCamera.World(FrameMin, FrameMax, Yaw, Pitch, Roll);
        var translation = SlotSize is { } size ? new Vector3(SlotCenter.X - size.X / 2, 0, size.Y / 2 - SlotCenter.Y)
            : new Vector3(InventoryCenter.X, 0, InventoryCenter.Y);
        return WeaponPakPreviewTransform.CreateModelSpace(-(_asset?.Diagnostics?.SourceOriginOffset ?? Vector3.Zero)) *
            WeaponPakPreviewTransform.CreateWorld(_scale, WeaponPakPreviewTransform.CreateRotation(_rotation), _offset) *
            (SlotSize is null ? Matrix4x4.CreateFromYawPitchRoll(Yaw, Pitch, Roll) : Matrix4x4.Identity) *
            Matrix4x4.CreateTranslation(translation);
    }

    private Matrix4x4 Projection(float aspect)
    {
        if (SlotSize is { } slot) return WeaponPakPreviewTransform.CreateViewProjection(slot.X, slot.Y);
        if (AssetPreview) return PreviewCamera.ViewProjection(Diameter, aspect, Zoom, GroundGrid);
        var height = Math.Max(160, 128 / aspect) * 1.18f / Zoom;
        return WeaponPakPreviewTransform.CreateViewProjection(height * aspect, height);
    }

    private void WriteScene(Span<float> values, float aspect, float elapsed)
    {
        values.Clear();
        var radius = Math.Max(96, _radius * (_scale == 0 ? 1 : MathF.Abs(_scale)) * 2);
        var distance = Math.Max(120, radius * 3.25f);
        // Models and characters share the game's Z-up light direction.
        var light = AssetPreview
            ? SolarLightingCalculator.Calculate(0).DirectionToLight
            : Vector3.Normalize(new Vector3(-radius * .35f, distance * .55f, radius * .85f));
        var camera = AssetPreview ? PreviewCamera.Position(Diameter, aspect, Zoom, GroundGrid) : new Vector3(0, distance, 0);
        MemoryMarshal.Write(MemoryMarshal.AsBytes(values), in light); values[3] = .10f;
        MemoryMarshal.Write(MemoryMarshal.AsBytes(values[4..]), in camera); values[7] = 16;
        values[8] = values[9] = values[10] = 1; values[11] = .38f;
        values[12] = 1; values[13] = .96f; values[14] = .88f; values[15] = .82f;
        values[16] = 203; values[18] = 300; values[19] = 80; values[20] = elapsed;
    }

    private void DrawGrid(Matrix4x4 world, Matrix4x4 projection)
    {
        var center = _mesh is null ? Vector3.Zero : (_min + _max) * .5f;
        var diameter = _fx is null ? Math.Max(1, Vector3.Distance(_min, _max)) : Diameter;
        var floor = _mesh is null ? 0 : _min.Z;
        var transform = Matrix4x4.CreateTranslation(center.X, center.Y, floor - diameter * .002f) * world * projection;
        Span<float> constants = stackalloc float[40]; constants.Clear();
        MemoryMarshal.Write(MemoryMarshal.AsBytes(constants), in transform);
        constants[16] = diameter * 1.5f; constants[17] = 32;
        constants[18] = diameter * 1.5f * .15f; constants[19] = diameter * 1.5f;
        constants[20] = .65f; constants[21] = .5f;
        WebGpuInterop.Draw(host, "", "grid", "", "", "", 0, 6, MemoryMarshal.AsBytes(constants));
    }
}
