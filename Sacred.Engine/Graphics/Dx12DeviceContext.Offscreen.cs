using System;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Latency;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics;

internal sealed partial class Dx12DeviceContext
{
    private int _offscreenFrameIndex;

    public Dx12DeviceContext(int width, int height, LowLatencySystem latency, int srvDescriptorCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _latency = latency;
        OutputWidth = RenderWidth = _requestedRenderWidth = _pendingOutputWidth = width;
        OutputHeight = RenderHeight = _requestedRenderHeight = _pendingOutputHeight = height;
        _hdrBrightnessSettings = HdrBrightnessSettings.Default;
        try
        {
            CreateDevice();
            CreateDescriptorHeaps(srvDescriptorCount);
            CreateBackBuffers();
            CreateDepthBuffer();
            CreateCommands();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void SetOffscreenSize(int width, int height)
    {
        if (_window is not null)
            throw new InvalidOperationException("This renderer owns a window.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _pendingOutputWidth = width;
        _pendingOutputHeight = height;
    }

    private ID3D12Resource CreateOffscreenTarget() => _device.CreateCommittedResource(
        new HeapProperties(HeapType.Default), HeapFlags.None,
        new ResourceDescription(ResourceDimension.Texture2D, 0, (ulong)OutputWidth, (uint)OutputHeight,
            1, 1, BackBufferFormat, 1, 0, TextureLayout.Unknown, ResourceFlags.AllowRenderTarget),
        ResourceStates.Present, new ClearValue(BackBufferFormat, new Color4(0, 0, 0, 1)));

    private void ResizeOffscreenIfNeeded(Action<Dx12FrameContext> releaseRetiredResources)
    {
        if (_pendingOutputWidth == OutputWidth && _pendingOutputHeight == OutputHeight)
            return;
        WaitForGpu(releaseRetiredResources);
        DisposeBackBuffers();
        DisposeSceneColor();
        OutputWidth = _pendingOutputWidth;
        OutputHeight = _pendingOutputHeight;
        RenderWidth = _requestedRenderWidth;
        RenderHeight = _requestedRenderHeight;
        _sceneResolutionChanged = false;
        RenderResolutionPercentage = (float)RenderHeight / OutputHeight;
        CreateBackBuffers();
        if (RenderWidth != OutputWidth || RenderHeight != OutputHeight)
            CreateSceneColor();
        CreateDepthBuffer();
    }
}
