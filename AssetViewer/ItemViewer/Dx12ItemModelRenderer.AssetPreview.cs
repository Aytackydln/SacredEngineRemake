using System;
using System.Numerics;
using Sacred.Granny.Assets;

namespace AssetViewer.ItemViewer;

internal sealed partial class Dx12ItemModelRenderer
{
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
        PreviewCamera.World(AssetFrameMin, AssetFrameMax, _userYaw, _userPitch, _userRoll);

    private Matrix4x4 CreateAssetViewProjectionMatrix() => PreviewCamera.ViewProjection(AssetPreviewDiameter(),
        Math.Max(.1f, _renderWidth / (float)Math.Max(1, _renderHeight)), _zoom, _groundGridEnabled);

    private float AssetPreviewDiameter() => PreviewCamera.Diameter(AssetFrameMin, AssetFrameMax);

    private Vector3 CreateAssetCameraPosition() => PreviewCamera.Position(AssetPreviewDiameter(),
        Math.Max(.1f, _renderWidth / (float)Math.Max(1, _renderHeight)), _zoom, _groundGridEnabled);
}
