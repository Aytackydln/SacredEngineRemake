using System;
using System.Numerics;
using Sacred.Granny.Assets;

namespace AssetViewer.ItemViewer;

internal sealed partial class Dx12ItemModelRenderer
{
    private const float AssetPreviewFieldOfView = MathF.PI / 4;
    private bool _assetPreview;
    private Vector3? _assetMin;
    private Vector3? _assetMax;
    private MeshBounds? _assetFrame;

    public void SetAssetPreview(bool enabled) => _assetPreview = enabled;

    public void SetAssetFrame(GrnAsset? baseModel)
    {
        _assetFrame = baseModel is null ? null : baseModel.Mesh is { Vertices.Length: > 0 } mesh
            ? CalculateBounds(mesh.Vertices) : new MeshBounds(Vector3.Zero, Vector3.Zero, Vector3.Zero, 1);
        if (_assetFrame is { } frame)
            Console.WriteLine($"[Assets] Actor frame: {baseModel!.Name}; min={frame.Min}; max={frame.Max}; " +
                $"rotation=<{_userYaw}, {_userPitch}, {_userRoll}>; zoom={_zoom}.");
    }

    private Vector3 AssetFrameMin => _assetFrame?.Min ?? _assetMin ?? _meshBounds.Min;
    private Vector3 AssetFrameMax => _assetFrame?.Max ?? _assetMax ?? _meshBounds.Max;

    private Matrix4x4 CreateAssetWorldMatrix() =>
        Matrix4x4.CreateTranslation(-(AssetFrameMin + AssetFrameMax) * 0.5f) *
        Matrix4x4.CreateRotationZ(_userYaw) *
        Matrix4x4.CreateRotationX(_userPitch) *
        Matrix4x4.CreateRotationY(_userRoll);

    private Matrix4x4 CreateAssetViewProjectionMatrix()
    {
        var aspect = Math.Max(0.1f, _renderWidth / (float)Math.Max(1, _renderHeight));
        var diameter = AssetPreviewDiameter();
        var height = diameter * Math.Max(1, 1 / aspect) * 1.15f / _zoom;
        // Model pipelines use Sacred.Engine's right-handed winding convention.
        // A left-handed camera mirrors the projected triangles and culls their
        // visible faces, making closed meshes appear sliced open from the inside.
        var camera = CreateAssetCameraPosition();
        var view = Matrix4x4.CreateLookAt(camera, Vector3.Zero, Vector3.UnitZ);
        if (_groundGridEnabled)
            return view * Matrix4x4.CreatePerspectiveFieldOfView(AssetPreviewFieldOfView, aspect,
                Math.Max(0.001f, diameter * 0.001f), camera.Length() + diameter * 4);
        return view * Matrix4x4.CreateOrthographic(height * aspect, height, 0.01f, diameter * 5);
    }

    private float AssetPreviewDiameter() =>
        Math.Max(1, Vector3.Distance(AssetFrameMin, AssetFrameMax));

    private Vector3 CreateAssetCameraPosition()
    {
        var diameter = AssetPreviewDiameter();
        if (_groundGridEnabled)
        {
            var aspect = Math.Max(0.1f, _renderWidth / (float)Math.Max(1, _renderHeight));
            // Fit the enclosing sphere to the tighter horizontal/vertical angle.
            // Zoom moves the camera; the field of view and ground geometry stay fixed.
            var fitHalfAngle = MathF.Atan(MathF.Tan(AssetPreviewFieldOfView * 0.5f) * Math.Min(1, aspect));
            var distance = diameter * 0.5f / MathF.Sin(fitHalfAngle) * 1.15f / _zoom;
            return Vector3.Normalize(new Vector3(0, -2, 0.65f)) * distance;
        }
        return new Vector3(0, -diameter * 2, diameter * 0.65f);
    }
}
