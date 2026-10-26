using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using AssetViewer.Browser.Rendering;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Sacred.Granny.Assets;
using Vector = Avalonia.Vector;

namespace AssetViewer.ItemViewer;

internal static class EquipmentSlotRenderFactory
{
    internal static IEquipmentSlotRenderBackend Create() => new BrowserSlotRenderer();

    private sealed class BrowserSlotRenderer : IEquipmentSlotRenderBackend
    {
        private readonly JSObject _host = WebGpuInterop.CreateHost(_ => { }, _ => { });
        private readonly Task _initialization;
        private readonly WebGpuPreviewScene _scene;
        private bool _disposed;
        internal BrowserSlotRenderer()
        {
            _scene = new WebGpuPreviewScene(_host);
            _initialization = WebGpuInterop.InitializeAsync(_host);
        }
        private GrnAsset? _asset;
        private Vector3 _rotation;
        private int _width, _height;
        private EquipmentEffectScene _effects = EquipmentEffectScene.Empty;
        public void SetInventoryPlacement(float scale, Vector3 offset) => _scene.SetPlacement(scale, offset);
        public void SetModel(GrnAsset asset, Vector3 rotation, int width, int height, EquipmentEffectScene effects)
        { _asset = asset; _rotation = rotation; _width = width; _height = height; _effects = effects; }
        public void SetEquipmentSlotViewport(Vector2 size, Vector2 center) { _scene.SlotSize = size; _scene.SlotCenter = center; }
        public async Task SetTexturesAsync(IReadOnlyDictionary<string, ModelTextureBinding> textures, CancellationToken token)
        {
            await _initialization.WaitAsync(token);
            ObjectDisposedException.ThrowIf(_disposed, this);
            _scene.SetModel(_asset!, _rotation, _width, _height, _effects);
            _scene.SetTextures(textures);
        }
        public async Task<Bitmap> CaptureBitmapAsync()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var size = _scene.SlotSize!.Value;
            var width = Math.Max(1, (int)MathF.Ceiling(size.X)); var height = Math.Max(1, (int)MathF.Ceiling(size.Y));
            _scene.Render(width, height);
            var pixels = await WebGpuInterop.ReadPixelsAsync(_host);
            var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
            using (var target = bitmap.Lock())
                for (var row = 0; row < height; row++) Marshal.Copy(pixels, row * width * 4, target.Address + row * target.RowBytes, width * 4);
            return bitmap;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; WebGpuInterop.Destroy(_host); _host.Dispose();
        }
    }
}
