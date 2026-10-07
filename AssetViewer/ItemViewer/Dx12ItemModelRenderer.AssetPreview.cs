using System;
using System.Numerics;

namespace AssetViewer.ItemViewer;

internal sealed partial class Dx12ItemModelRenderer
{
    private bool _assetPreview;
    private Vector3? _assetMin;
    private Vector3? _assetMax;

    public void SetAssetPreview(bool enabled) => _assetPreview = enabled;

    private Matrix4x4 CreateAssetWorldMatrix() =>
        Matrix4x4.CreateTranslation(-((_assetMin ?? _meshBounds.Min) + (_assetMax ?? _meshBounds.Max)) * 0.5f) *
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
        var view = Matrix4x4.CreateLookAt(CreateAssetCameraPosition(), Vector3.Zero, Vector3.UnitZ);
        return view * Matrix4x4.CreateOrthographic(height * aspect, height, 0.01f, diameter * 5);
    }

    private float AssetPreviewDiameter() =>
        Math.Max(1, Vector3.Distance(_assetMin ?? _meshBounds.Min, _assetMax ?? _meshBounds.Max));

    private Vector3 CreateAssetCameraPosition()
    {
        var diameter = AssetPreviewDiameter();
        return new Vector3(0, -diameter * 2, diameter * 0.65f);
    }
}
