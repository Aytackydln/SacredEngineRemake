using System.Numerics;

namespace AssetViewer.ItemViewer;

internal sealed partial class Dx12ItemModelRenderer
{
    private Vector2? _equipmentSlotSize;
    private Vector2 _equipmentItemCenter;

    public void SetEquipmentSlotViewport(Vector2 size, Vector2 itemCenter)
    {
        _equipmentSlotSize = size;
        _equipmentItemCenter = itemCenter;
    }

    private Matrix4x4 CreateEquipmentSlotWorldMatrix()
    {
        var size = _equipmentSlotSize!.Value;
        // cUI_Equipment::render places the item's grid center at slot origin +
        // (w * 16, h * 16), rather than fitting its model bounds to the control.
        return WeaponPakPreviewTransform.CreateModelSpace(-_sourceOriginOffset) *
            WeaponPakPreviewTransform.CreateWorld(_previewScale,
                WeaponPakPreviewTransform.CreateRotation(_previewRotation), _previewOffset) *
            Matrix4x4.CreateTranslation(_equipmentItemCenter.X - size.X / 2, 0,
                size.Y / 2 - _equipmentItemCenter.Y);
    }
}
