using System.Numerics;
using System.Runtime.InteropServices.JavaScript;
using AssetViewer.AssetViewer;
using AssetViewer.ItemViewer;
using Avalonia.Browser;
using Avalonia.Controls;
using Avalonia.Platform;
using Sacred.Granny.Assets;

namespace AssetViewer.Browser.Rendering;

internal sealed class WebGpuModelViewportHost : NativeControlHost, IModelViewport
{
    internal static (int Width, int Height)? CaptureSize { get; set; }
    private static WeakReference<WebGpuModelViewportHost>? _animatedViewport;
    private JSObject? _host;
    private WebGpuPreviewScene? _scene;
    private TaskCompletionSource _ready = new();
    private GrnAsset? _asset, _frame;
    private EquipmentEffectScene _effects = EquipmentEffectScene.Empty;
    private FxPreviewPlayback? _fx;
    private IReadOnlyDictionary<string, ModelTextureBinding> _textures = new Dictionary<string, ModelTextureBinding>();
    private Vector3 _rotation, _offset;
    private int _width = 1, _height = 1;
    private float _scale = 1, _yaw, _pitch, _roll, _zoom = 1.12f;
    private bool _assetPreview, _ground, _playing = true, _zoomEnabled = true;
    private float? _time;
    private int _generation;
    private (int Width, int Height)? _emptyFrameSize;
    public event Action<float>? HorizontalRotationChanged;

    internal WebGpuModelViewportHost()
    {
        ClipToBounds = true;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var host = WebGpuInterop.CreateHost(value => RotateHorizontally((float)value), ZoomBy);
        _host = host;
        _ = InitializeAsync(host);
        return new JSObjectControlHandle(host);
    }

    private async Task InitializeAsync(JSObject host)
    {
        try
        {
            await WebGpuInterop.InitializeAsync(host);
            if (!ReferenceEquals(_host, host)) return;
            _scene = new WebGpuPreviewScene(host)
            { AssetPreview = _assetPreview, GroundGrid = _ground, AnimationPlaying = _playing,
                Yaw = _yaw, Pitch = _pitch, Roll = _roll, Zoom = _zoom };
            _scene.SetPlacement(_scale, _offset);
            if (_asset is not null) _scene.SetModel(_asset, _rotation, _width, _height, _effects);
            _scene.SetFrame(_frame); _scene.SetFx(_fx);
            if (_time is { } time) _scene.SetTime(time);
            _scene.SetTextures(_textures);
            _ready.TrySetResult(); WebGpuInterop.StartFrames(host, _ => Render());
        }
        catch (Exception error)
        {
            _ready.TrySetException(error);
            Console.WriteLine($"[WebGPU] Preview initialization failed: {error}");
        }
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_animatedViewport?.TryGetTarget(out var animated) == true && ReferenceEquals(animated, this)) _animatedViewport = null;
        _scene = null; _emptyFrameSize = null; _generation++;
        _ready.TrySetCanceled(); _ready = new();
        if (_host is { } host) { WebGpuInterop.Destroy(host); host.Dispose(); _host = null; }
    }

    private void Render()
    {
        if (_scene is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var size = (Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale)), Math.Max(1, (int)Math.Ceiling(Bounds.Height * scale)));
        if (_scene.IsEmpty && _emptyFrameSize == size) return;
        try
        {
            _scene.Render(size.Item1, size.Item2);
            _emptyFrameSize = _scene.IsEmpty ? size : null;
        }
        catch (Exception error)
        {
            if (_host is { } host) WebGpuInterop.StopFrames(host);
            Console.WriteLine($"[WebGPU] Rendering stopped: {error}");
        }
    }

    public void ClearModel()
    {
        _generation++; _emptyFrameSize = null; _asset = _frame = null; _fx = null; _effects = EquipmentEffectScene.Empty;
        _textures = new Dictionary<string, ModelTextureBinding>(); _scene?.Clear();
    }

    public void ShowModel(GrnAsset asset, Vector3 previewRotation, int gridWidth, int gridHeight, EquipmentEffectScene effectScene)
    {
        if (_assetPreview && asset.Skin is not null && asset.DefaultAnimation is not null) _animatedViewport = new(this);
        _generation++; _emptyFrameSize = null; _asset = asset; _rotation = previewRotation; _width = gridWidth; _height = gridHeight;
        _effects = effectScene; _fx = null; _time = null; _textures = new Dictionary<string, ModelTextureBinding>();
        _scene?.SetModel(asset, previewRotation, gridWidth, gridHeight, effectScene);
    }

    public async Task ShowTexturesAsync(IReadOnlyDictionary<string, ModelTextureBinding> textures, CancellationToken cancellationToken = default)
    {
        var generation = _generation;
        await _ready.Task.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (generation != _generation) return;
        _textures = textures; _scene?.SetTextures(textures);
    }

    public void SetAssetPreview(bool enabled) { _emptyFrameSize = null; _assetPreview = enabled; if (_scene is { } scene) scene.AssetPreview = enabled; }
    public void SetZoomEnabled(bool enabled) => _zoomEnabled = enabled;
    public void SetGroundGridEnabled(bool enabled) { _ground = enabled; if (_scene is { } scene) scene.GroundGrid = enabled; }
    public void SetAssetFrame(GrnAsset? baseModel) { _frame = baseModel; _scene?.SetFrame(baseModel); }
    public void SetFxPreview(FxPreviewPlayback? preview) { _emptyFrameSize = null; _fx = preview; _scene?.SetFx(preview); }
    public void SetInventoryPlacement(float scale, Vector3 offset) { _emptyFrameSize = null; _scale = scale; _offset = offset; _scene?.SetPlacement(scale, offset); }
    public void SetAnimationPlaying(bool playing) { _playing = playing; if (_scene is { } scene) scene.AnimationPlaying = playing; }
    public void SetAnimationTime(float seconds) { _time = seconds; _scene?.SetTime(seconds); }
    public void SetUserRotation(float yaw, float pitch, float roll)
    {
        _yaw = yaw; _pitch = pitch; _roll = roll;
        if (_scene is { } scene) { scene.Yaw = yaw; scene.Pitch = pitch; scene.Roll = roll; }
    }
    public void RotateHorizontally(float radians)
    {
        if (!_assetPreview) return;
        if (!float.IsFinite(radians)) throw new ArgumentOutOfRangeException(nameof(radians));
        SetUserRotation((float)Math.IEEERemainder(_yaw + (double)radians, Math.Tau), _pitch, _roll);
        HorizontalRotationChanged?.Invoke(_yaw);
    }
    public void ZoomBy(double delta)
    {
        if (!double.IsFinite(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
        if (!_zoomEnabled) return;
        _zoom = Math.Clamp(_zoom * (float)Math.Pow(1.12, delta), .45f, 3.5f);
        if (_scene is { } scene) scene.Zoom = _zoom;
    }
    public void SaveScreenshot(string path)
    {
        if (_scene is null || _host is null) throw new InvalidOperationException("WebGPU is still initializing.");
        _emptyFrameSize = null;
        if (CaptureSize is { } size) _scene.Render(size.Width, size.Height); else Render();
        WebGpuInterop.Screenshot(_host, path);
    }

    internal static async Task<string> ValidateCurrentSkinningAsync()
    {
        if (_animatedViewport is null || !_animatedViewport.TryGetTarget(out var viewport) || viewport._scene is not { } scene)
            throw new InvalidOperationException("Select an animated character first.");
        viewport.Render();
        return await scene.ValidateSkinningAsync();
    }
}
