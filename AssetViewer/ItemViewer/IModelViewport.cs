using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AssetViewer.AssetViewer;
using Sacred.Granny.Assets;

namespace AssetViewer.ItemViewer;

internal interface IModelViewport
{
    event Action<float>? HorizontalRotationChanged;
    void RotateHorizontally(float radians);
    void SetAnimationPlaying(bool playing);
    void SetAnimationTime(float seconds);
    void SetAssetPreview(bool enabled);
    void SetZoomEnabled(bool enabled);
    void SetGroundGridEnabled(bool enabled);
    void SetAssetFrame(GrnAsset? baseModel);
    void ClearModel();

    void ShowModel(GrnAsset asset,
        Vector3 previewRotation,
        int gridWidth,
        int gridHeight,
        EquipmentEffectScene effectScene);

    void SetUserRotation(float yaw, float pitch, float roll);
    void SetFxPreview(FxPreviewPlayback? preview);
    void SetInventoryPlacement(float scale, Vector3 offset);
    void SaveScreenshot(string path);

    Task ShowTexturesAsync(
        IReadOnlyDictionary<string, ModelTextureBinding> textures,
        CancellationToken cancellationToken = default);

    void ZoomBy(double delta);
}
