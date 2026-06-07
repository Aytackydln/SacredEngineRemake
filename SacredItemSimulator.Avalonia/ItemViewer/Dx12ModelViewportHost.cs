using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Sacred.Assets;
using Sacred.Granny;

namespace SacredItemSimulator.Avalonia.ItemViewer;

internal sealed class Dx12ModelViewportHost : NativeControlHost
{
    private readonly DispatcherTimer _renderTimer;
    private EmbeddedRenderWindow? _window;
    private Dx12ItemModelRenderer? _renderer;
    private GrnAsset? _pendingAsset;
    private Vector3 _pendingPreviewRotation;
    private int _pendingGridWidth = 1;
    private int _pendingGridHeight = 1;
    private IReadOnlyDictionary<string, TextureAsset> _pendingTextures = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);
    private float _pendingYaw;
    private float _pendingPitch;
    private float _pendingRoll;

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
        _pendingTextures = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);
        _renderer?.ClearModel();
    }

    public void ShowModel(GrnAsset asset, Vector3 previewRotation, int gridWidth, int gridHeight)
    {
        _pendingAsset = asset;
        _pendingPreviewRotation = previewRotation;
        _pendingGridWidth = gridWidth;
        _pendingGridHeight = gridHeight;
        _pendingTextures = new Dictionary<string, TextureAsset>(StringComparer.OrdinalIgnoreCase);
        _renderer?.SetModel(asset, previewRotation, gridWidth, gridHeight);
        _renderer?.SetUserRotation(_pendingYaw, _pendingPitch, _pendingRoll);
    }

    public void SetUserRotation(float yaw, float pitch, float roll)
    {
        _pendingYaw = yaw;
        _pendingPitch = pitch;
        _pendingRoll = roll;
        _renderer?.SetUserRotation(yaw, pitch, roll);
    }

    public void ShowTextures(IReadOnlyDictionary<string, TextureAsset> textures)
    {
        _pendingTextures = textures;
        _renderer?.SetTextures(textures);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _window = new EmbeddedRenderWindow(parent.Handle, OnNativeMouseWheel);
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

        if (_pendingAsset is not null)
            _renderer.SetModel(_pendingAsset, _pendingPreviewRotation, _pendingGridWidth, _pendingGridHeight);
        else
            _renderer.ClearModel();

        _renderer.SetUserRotation(_pendingYaw, _pendingPitch, _pendingRoll);
        if (_pendingTextures.Count > 0)
            _renderer.SetTextures(_pendingTextures);
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

    private void ZoomBy(double delta)
    {
        _renderer?.ZoomBy(delta);
    }
}
