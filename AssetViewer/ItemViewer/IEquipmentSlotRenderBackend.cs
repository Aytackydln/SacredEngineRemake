using System.Numerics;
using Avalonia.Media.Imaging;
using Sacred.Granny.Assets;

namespace AssetViewer.ItemViewer;

internal interface IEquipmentSlotRenderBackend : IDisposable
{
    void SetInventoryPlacement(float scale, Vector3 offset);
    void SetModel(GrnAsset asset, Vector3 rotation, int width, int height, EquipmentEffectScene effects);
    void SetEquipmentSlotViewport(Vector2 size, Vector2 center);
    Task SetTexturesAsync(IReadOnlyDictionary<string, ModelTextureBinding> textures, CancellationToken token);
    Task<Bitmap> CaptureBitmapAsync();
}
