using System;
using System.Diagnostics;
using Sacred.Engine.Graphics.Frames;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics;

internal sealed partial class Dx12DeviceContext
{
    private static readonly TimeSpan ResizeDebounce = TimeSpan.FromMilliseconds(150);

    private void CreateSceneColor()
    {
        var description = new ResourceDescription(ResourceDimension.Texture2D, 0, (ulong)RenderWidth, (uint)RenderHeight,
            1, 1, BackBufferFormat, 1, 0, TextureLayout.Unknown,
            ResourceFlags.AllowRenderTarget);
        var clear = new ClearValue(BackBufferFormat, new Color4(0, 0, 0, 1));
        _sceneColor = _device.CreateCommittedResource(new HeapProperties(HeapType.Default, 0, 0), HeapFlags.None,
            description, ResourceStates.PixelShaderResource, clear);
        _device.CreateRenderTargetView(_sceneColor, null, SceneRenderTarget);
        _device.CreateShaderResourceView(_sceneColor, null, SceneColorSrvCpuHandle);
    }

    private void DisposeSceneColor()
    {
        _sceneColor?.Dispose();
        _sceneColor = null;
    }

    private void RecreateSceneColorIfNeeded(Action<Dx12FrameContext> releaseRetiredResources)
    {
        if (!_sceneResolutionChanged)
            return;

        _sceneResolutionChanged = false;

        WaitForGpu(releaseRetiredResources);
        _depthBuffer?.Dispose();
        _depthBuffer = null;
        DisposeSceneColor();

        RenderWidth = _requestedRenderWidth;
        RenderHeight = _requestedRenderHeight;
        RenderResolutionPercentage = (float)RenderHeight / Math.Max(1, OutputHeight);
        if (_window is not null || RenderWidth != OutputWidth || RenderHeight != OutputHeight)
            CreateSceneColor();
        CreateDepthBuffer();
    }

    private void ResizeIfNeeded(Action<Dx12FrameContext> releaseRetiredResources)
    {
        if (_window is null)
        {
            ResizeOffscreenIfNeeded(releaseRetiredResources);
            return;
        }
        var width = _window.ClientWidth;
        var height = _window.ClientHeight;
        if (width <= 0 || height <= 0)
            return;
        if (width == OutputWidth && height == OutputHeight)
        {
            return;
        }
        if (width != _pendingOutputWidth || height != _pendingOutputHeight)
        {
            _pendingOutputWidth = width;
            _pendingOutputHeight = height;
            _lastResizeRequestTimestamp = Stopwatch.GetTimestamp();
            return;
        }
        if (Stopwatch.GetElapsedTime(_lastResizeRequestTimestamp) < ResizeDebounce)
            return;

        WaitForGpu(releaseRetiredResources);
        DisposeBackBuffers();
        _swapChain.ResizeBuffers(FrameCount, _pendingOutputWidth, _pendingOutputHeight, _swapChainFlags);
        OutputWidth = _pendingOutputWidth;
        OutputHeight = _pendingOutputHeight;
        RenderResolutionPercentage = (float)RenderHeight / _pendingOutputHeight;
        CreateBackBuffers();
        OutputResized?.Invoke();
        EngineLog.WriteLine($"Viewport resized: output {OutputWidth}x{OutputHeight}, requested scene {_requestedRenderWidth}x{_requestedRenderHeight}.");
    }

}
