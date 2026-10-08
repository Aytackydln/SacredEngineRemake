using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Sacred.Granny.Assets;

namespace AssetViewer.ItemViewer;

internal sealed class Dx12ModelViewportHost : NativeControlHost
{
    private readonly DispatcherTimer _renderTimer;
    private EmbeddedRenderWindow? _window;
    private Dx12ItemModelRenderer? _renderer;
    private GrnAsset? _pendingAsset;
    private Vector3 _pendingPreviewRotation;
    private EquipmentEffectScene _pendingEffectScene = EquipmentEffectScene.Empty;
    private int _pendingGridWidth = 1;
    private int _pendingGridHeight = 1;
    private IReadOnlyDictionary<string, ModelTextureBinding> _pendingTextures = new Dictionary<string, ModelTextureBinding>(StringComparer.OrdinalIgnoreCase);
    private float _pendingYaw;
    private float _pendingPitch;
    private float _pendingRoll;
    private float _pendingPreviewScale = 1.0f;
    private Vector3 _pendingPreviewOffset;
    private bool _assetPreview;
    private bool _animationPlaying = true;
    private float? _animationTime;
    public event Action<float>? HorizontalRotationChanged;

    public void RotateHorizontally(float radians)
    {
        if (!_assetPreview) return;
        if (!float.IsFinite(radians)) throw new ArgumentOutOfRangeException(nameof(radians));
        var yaw = (float)Math.IEEERemainder(_pendingYaw + (double)radians, Math.Tau);
        SetUserRotation(yaw, _pendingPitch, _pendingRoll);
        HorizontalRotationChanged?.Invoke(yaw);
    }

    public void SetAnimationPlaying(bool playing)
    {
        _animationPlaying = playing;
        _renderer?.SetAnimationPlaying(playing);
    }

    public void SetAnimationTime(float seconds)
    {
        _animationTime = seconds;
        _renderer?.SetAnimationTime(seconds);
    }

    public void SetAssetPreview(bool enabled)
    {
        _assetPreview = enabled;
        _window?.SetHorizontalDragEnabled(enabled);
        _renderer?.SetAssetPreview(enabled);
    }

    public Dx12ModelViewportHost()
    {
        Focusable = true;
        ClipToBounds = true;
        _renderTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => RenderFrame());
        PointerPressed += (_, _) => Focus();
        PointerWheelChanged += OnPointerWheelChanged;
        DetachedFromVisualTree += (_, _) => StopRenderer();
    }

    public void ClearModel()
    {
        _pendingAsset = null;
        _pendingEffectScene = EquipmentEffectScene.Empty;
        _pendingTextures = new Dictionary<string, ModelTextureBinding>(StringComparer.OrdinalIgnoreCase);
        _renderer?.ClearModel();
    }

    public void ShowModel(GrnAsset asset,
        Vector3 previewRotation,
        int gridWidth,
        int gridHeight,
        EquipmentEffectScene effectScene)
    {
        _pendingAsset = asset;
        _animationTime = null;
        _pendingPreviewRotation = previewRotation;
        _pendingEffectScene = effectScene;
        _pendingGridWidth = gridWidth;
        _pendingGridHeight = gridHeight;
        _pendingTextures = new Dictionary<string, ModelTextureBinding>(StringComparer.OrdinalIgnoreCase);
        _renderer?.SetModel(asset, previewRotation, gridWidth, gridHeight, effectScene);
        _renderer?.SetAnimationPlaying(_animationPlaying);
        _renderer?.SetUserRotation(_pendingYaw, _pendingPitch, _pendingRoll);
    }

    public void SetUserRotation(float yaw, float pitch, float roll)
    {
        _pendingYaw = yaw;
        _pendingPitch = pitch;
        _pendingRoll = roll;
        _renderer?.SetUserRotation(yaw, pitch, roll);
    }

    public void SetInventoryPlacement(float scale, Vector3 offset)
    {
        _pendingPreviewScale = scale;
        _pendingPreviewOffset = offset;
        _renderer?.SetInventoryPlacement(scale, offset);
    }

    public void SaveScreenshot(string path) =>
        (_renderer ?? throw new InvalidOperationException("The inventory renderer is not ready.")).SaveScreenshot(path);

    public async Task ShowTexturesAsync(
        IReadOnlyDictionary<string, ModelTextureBinding> textures,
        CancellationToken cancellationToken = default)
    {
        _pendingTextures = textures;
        if (_renderer is not null)
            await _renderer.SetTexturesAsync(textures, cancellationToken);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _window = new EmbeddedRenderWindow(parent.Handle, OnNativeMouseWheel, OnNativeHorizontalDrag);
        _window.SetHorizontalDragEnabled(_assetPreview);
        _renderer = new Dx12ItemModelRenderer(_window.Hwnd);
        ApplyPendingState();
        _renderTimer.Start();
        return new PlatformHandle(_window.Hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        StopRenderer();
        base.DestroyNativeControlCore(control);
    }

    private void ApplyPendingState()
    {
        if (_renderer is null)
            return;

        _renderer.SetInventoryPlacement(_pendingPreviewScale, _pendingPreviewOffset);
        _renderer.SetAssetPreview(_assetPreview);
        if (_pendingAsset is not null)
            _renderer.SetModel(_pendingAsset, _pendingPreviewRotation, _pendingGridWidth, _pendingGridHeight, _pendingEffectScene);
        else
            _renderer.ClearModel();

        _renderer.SetUserRotation(_pendingYaw, _pendingPitch, _pendingRoll);
        _renderer.SetAnimationPlaying(_animationPlaying);
        if (_animationTime is { } time) _renderer.SetAnimationTime(time);
        if (_pendingTextures.Count > 0)
            _ = _renderer.SetTexturesAsync(_pendingTextures);
    }

    private void RenderFrame()
    {
        if (_renderer is null)
            return;

        try
        {
            _renderer.RenderFrame();
        }
        catch
        {
            StopRenderer();
            throw;
        }
    }

    private void StopRenderer()
    {
        _renderTimer.Stop();
        _renderer?.Dispose();
        _renderer = null;
        _window?.Dispose();
        _window = null;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        ZoomBy(e.Delta.Y);
        e.Handled = true;
    }

    private void OnNativeMouseWheel(int delta)
    {
        Dispatcher.UIThread.Post(() => ZoomBy(delta / 120.0), DispatcherPriority.Input);
    }

    private void OnNativeHorizontalDrag(int delta)
    {
        // Win32 supplies physical pixels; keep sensitivity consistent across display scales.
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        RotateHorizontally((float)(delta * 0.01 / scale));
    }

    private void ZoomBy(double delta)
    {
        _renderer?.ZoomBy(delta);
    }
}
