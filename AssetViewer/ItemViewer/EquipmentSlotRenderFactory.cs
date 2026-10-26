using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Sacred.Granny.Assets;

namespace AssetViewer.ItemViewer;

internal static class EquipmentSlotRenderFactory
{
    internal static IEquipmentSlotRenderBackend Create() => new DesktopSlotRenderer();

    private sealed class DesktopSlotRenderer : IEquipmentSlotRenderBackend
    {
        private EmbeddedRenderWindow? _window;
        private Dx12ItemModelRenderer? _renderer;
        private Dx12ItemModelRenderer Renderer
        {
            get
            {
                _window ??= new EmbeddedRenderWindow(0, _ => { }, visible: false, width: 256, height: 256);
                return _renderer ??= new Dx12ItemModelRenderer(_window.Hwnd);
            }
        }
        public void SetInventoryPlacement(float scale, Vector3 offset) => Renderer.SetInventoryPlacement(scale, offset);
        public void SetModel(GrnAsset asset, Vector3 rotation, int width, int height, EquipmentEffectScene effects) => Renderer.SetModel(asset, rotation, width, height, effects);
        public void SetEquipmentSlotViewport(Vector2 size, Vector2 center) => Renderer.SetEquipmentSlotViewport(size, center);
        public Task SetTexturesAsync(IReadOnlyDictionary<string, ModelTextureBinding> textures, CancellationToken token) => Renderer.SetTexturesAsync(textures, token);
        public Task<Bitmap> CaptureBitmapAsync() => Task.FromResult(Renderer.CaptureBitmap());
        public void Dispose() { _renderer?.Dispose(); _window?.Dispose(); }
    }
}
