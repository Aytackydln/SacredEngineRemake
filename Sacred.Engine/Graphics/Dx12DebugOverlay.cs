using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Core.World.Sector;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene.InGame;
using Sacred.Shaders;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics;

public unsafe class Dx12DebugOverlay : IDisposable
{
    private const int DebugOverlayX = 12;
    private const int DebugOverlayY = 12;
    private const int OverlayWidth = 440;
    private const int OverlayHeight = 52;

    private readonly DebugOverlayFontSet _fonts;
    private readonly DebugTextOverlay _debugOverlay;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly Dx12TextureUploader _textureUploader;
    private readonly CpuDescriptorHandle _debugOverlayCpuHandle;
    private readonly GpuDescriptorHandle _debugOverlayGpuHandle;
    private readonly WorldQuadShaderConstantsUpdater _worldQuadShaderConstants = new();

    private bool _debugOverlayDirty;
    private ID3D12Resource? _debugOverlayTexture;
    private ResourceStates _debugOverlayState = ResourceStates.Common;

    private int _framesSinceTitleUpdate;
    private double _lastTitleUpdateSeconds;
    private double _fps;
    private Task? _debugRasterTask;

    public Dx12DebugOverlay(
        ID3D12GraphicsCommandList commandList,
        Dx12TextureUploader textureUploader,
        CpuDescriptorHandle debugOverlayCpuHandle,
        GpuDescriptorHandle debugOverlayGpuHandle)
    {
        _commandList = commandList;
        _textureUploader = textureUploader;
        _fonts = DebugOverlayFontSet.LoadDebug();
        _debugOverlay = new DebugTextOverlay(_fonts, OverlayWidth, OverlayHeight);
        _debugOverlayCpuHandle = debugOverlayCpuHandle;
        _debugOverlayGpuHandle = debugOverlayGpuHandle;
    }

    public double FramesPerSecond => _fps;

    public void Update(
        SacredCamera camera,
        VisibleWorld world,
        Dx12DebugOverlayStats rendererStats,
        List<ID3D12Resource> transientResources)
    {
        CompleteDebugRasterIfReady();
        UploadOverlayIfNeeded(
            _debugOverlay,
            ref _debugOverlayDirty,
            ref _debugOverlayTexture,
            ref _debugOverlayState,
            _debugOverlayCpuHandle,
            transientResources);
        QueueDebugRaster(camera, world, rendererStats);
    }

    private void UploadOverlayIfNeeded(
        DebugTextOverlay overlay,
        ref bool dirty,
        ref ID3D12Resource? texture,
        ref ResourceStates state,
        CpuDescriptorHandle cpuHandle,
        List<ID3D12Resource> transientResources)
    {
        if (!dirty)
            return;

        if (texture is null)
        {
            texture = _textureUploader.UploadRgbaTexture(
                _commandList,
                overlay.Width,
                overlay.Height,
                overlay.Rgba,
                transientResources);
            _textureUploader.CreateShaderResourceView(texture, cpuHandle);
            state = ResourceStates.PixelShaderResource;
        }
        else
        {
            state = _textureUploader.UpdateRgbaTexture(
                _commandList,
                texture,
                overlay.Width,
                overlay.Height,
                overlay.Rgba,
                state,
                transientResources);
        }

        dirty = false;
    }

    public void RecordDebugOverlay(int renderWidth, int renderHeight, float uiPaperWhiteNits)
    {
        RecordOverlay(
            _debugOverlayTexture,
            _debugOverlayState,
            _debugOverlayGpuHandle,
            DebugOverlayX,
            DebugOverlayY,
            renderWidth,
            renderHeight,
            uiPaperWhiteNits);

    }

    private void RecordOverlay(
        ID3D12Resource? texture,
        ResourceStates state,
        GpuDescriptorHandle gpuHandle,
        float x,
        float y,
        int renderWidth,
        int renderHeight,
        float uiPaperWhiteNits)
    {
        if (texture is null || state != ResourceStates.PixelShaderResource)
            return;

        var width = Math.Min(_debugOverlay.Width, Math.Max(0, renderWidth - x - DebugOverlayX));
        var height = Math.Min(_debugOverlay.Height, Math.Max(0, renderHeight - y - DebugOverlayY));

        if (width <= 0.0f || height <= 0.0f)
            return;

        var constants = stackalloc float[WorldQuadShaderLayout.RootConstantsCount];
        _worldQuadShaderConstants.Write(
            constants,
            new WorldQuadShaderConstants(
                new Vector4(x, y, width, height),
                new Vector2(renderWidth, renderHeight),
                AmbientColour: Vector3.One,
                IsPremultipliedAlpha: false,
                PaperWhiteNits: uiPaperWhiteNits));

        _commandList.SetGraphicsRoot32BitConstants(
            WorldQuadShaderLayout.RootConstantsRootParameter,
            WorldQuadShaderLayout.RootConstantsCount,
            constants,
            0);
        _commandList.SetGraphicsRootDescriptorTable(WorldQuadShaderLayout.TextureRootParameter, gpuHandle);
        _commandList.DrawInstanced(6, 1, 0, 0);
    }

    private void QueueDebugRaster(
        SacredCamera camera,
        VisibleWorld world,
        Dx12DebugOverlayStats rendererStats)
    {
        _framesSinceTitleUpdate++;
        if (_debugRasterTask is not null || _debugOverlayDirty)
            return;

        var now = _clock.Elapsed.TotalSeconds;
        var elapsed = now - _lastTitleUpdateSeconds;
        if (elapsed < 0.5 && _debugOverlayTexture is not null)
            return;

        if (elapsed >= 0.5)
        {
            _fps = _framesSinceTitleUpdate / elapsed;
            _framesSinceTitleUpdate = 0;
            _lastTitleUpdateSeconds = now;
        }

        var lines = new[]
        {
            $"FPS {_fps:0.0}  FRAME {(_fps > 0.0 ? 1000.0 / _fps : 0.0):0.00} MS  {rendererStats.FramePacingStatus}",
            $"WORLD {camera.WorldCenter.X:0.00}, {camera.WorldCenter.Y:0.00}  SECTOR {world.CenterSector.X}, {world.CenterSector.Y}"
        };

        _debugRasterTask = Task.Run(() => _debugOverlay.SetLines(lines));
    }

    private void CompleteDebugRasterIfReady()
    {
        if (_debugRasterTask is not { IsCompleted: true } completed)
            return;

        completed.GetAwaiter().GetResult();
        _debugRasterTask = null;
        _debugOverlayDirty = true;
    }

    public void Dispose()
    {
        _debugRasterTask?.GetAwaiter().GetResult();
        _debugRasterTask = null;
        _debugOverlayTexture?.Dispose();
        _debugOverlayTexture = null;
        _fonts.Dispose();
    }
}

public readonly record struct Dx12DebugOverlayStats(
    int GpuSectorTextureCount,
    int MaxSectorTextureCount,
    int PendingSectorUploadCount,
    int ReadyModelTextureCount,
    int LoadingModelTextureCount,
    int UploadingModelTextureCount,
    int FailedModelTextureCount,
    int VisibleLiquidSpriteCount,
    int VisibleStaticSpriteCount,
    int VisibleStaticShadowCount,
    int StaticShadowDrawCallCount,
    int LegacyShadowDrawCallCount,
    int CandidateHaloCount,
    int VisibleHaloCount,
    int SurfaceLightCount,
    string FramePacingStatus);
