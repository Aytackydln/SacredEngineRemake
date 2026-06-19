using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sacred.Core.World;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics;

public unsafe class Dx12DebugOverlay : IDisposable
{
    private const int DebugOverlayX = 12;
    private const int DebugOverlayY = 12;
    private const int ControlsOverlayX = 12;
    private const int ControlsOverlayBottomMargin = 12;

    private readonly DebugOverlayFontSet _fonts;
    private readonly DebugTextOverlay _debugOverlay;
    private readonly DebugTextOverlay _controlsOverlay;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly Dx12TextureUploader _textureUploader;
    private readonly TerrainRenderer _terrain;
    private readonly CpuDescriptorHandle _debugOverlayCpuHandle;
    private readonly GpuDescriptorHandle _debugOverlayGpuHandle;
    private readonly CpuDescriptorHandle _controlsOverlayCpuHandle;
    private readonly GpuDescriptorHandle _controlsOverlayGpuHandle;
    private readonly List<ID3D12Resource> _uploadResources;

    private bool _debugOverlayDirty = true;
    private bool _controlsOverlayDirty = true;
    private ID3D12Resource? _debugOverlayTexture;
    private ID3D12Resource? _controlsOverlayTexture;
    private ResourceStates _debugOverlayState = ResourceStates.Common;
    private ResourceStates _controlsOverlayState = ResourceStates.Common;

    private int _framesSinceTitleUpdate;
    private double _lastTitleUpdateSeconds;
    private double _fps;

    public Dx12DebugOverlay(
        ID3D12GraphicsCommandList commandList,
        Dx12TextureUploader textureUploader,
        TerrainRenderer terrain,
        string gameDirectory,
        CpuDescriptorHandle debugOverlayCpuHandle,
        GpuDescriptorHandle debugOverlayGpuHandle,
        CpuDescriptorHandle controlsOverlayCpuHandle,
        GpuDescriptorHandle controlsOverlayGpuHandle)
    {
        _commandList = commandList;
        _textureUploader = textureUploader;
        _terrain = terrain;
        _fonts = DebugOverlayFontSet.Load(gameDirectory);
        _debugOverlay = new DebugTextOverlay(_fonts);
        _controlsOverlay = new DebugTextOverlay(_fonts);
        _debugOverlayCpuHandle = debugOverlayCpuHandle;
        _debugOverlayGpuHandle = debugOverlayGpuHandle;
        _controlsOverlayCpuHandle = controlsOverlayCpuHandle;
        _controlsOverlayGpuHandle = controlsOverlayGpuHandle;
        _uploadResources = [];
        _controlsOverlay.SetLines(
        [
            DebugTextLine.CarolingTitle("CONTROLS"),
            DebugTextLine.Default("MOVE: WASD, ARROWS, LEFT STICK"),
            DebugTextLine.Default("FASTER: SHIFT OR GAMEPAD A"),
            DebugTextLine.Default("CYCLE: TAB, MOUSE4/5, GAMEPAD B"),
            DebugTextLine.Default("ZOOM: Q/E, WHEEL, RIGHT STICK"),
            DebugTextLine.Default("TOGGLE HDR: F4"),
            DebugTextLine.Default("FRAME PACING: F5"),
            DebugTextLine.Default("LOW LATENCY: F6")
        ]);
    }

    public void Update(SacredCamera camera, VisibleWorld world, SceneState scene, Dx12DebugOverlayStats rendererStats)
    {
        DisposeUploadResources();
        UpdateDebugInfo(camera, world, scene, rendererStats);
        UploadOverlayIfNeeded(
            _debugOverlay,
            ref _debugOverlayDirty,
            ref _debugOverlayTexture,
            ref _debugOverlayState,
            _debugOverlayCpuHandle);
        UploadOverlayIfNeeded(
            _controlsOverlay,
            ref _controlsOverlayDirty,
            ref _controlsOverlayTexture,
            ref _controlsOverlayState,
            _controlsOverlayCpuHandle);
    }

    private void UploadOverlayIfNeeded(
        DebugTextOverlay overlay,
        ref bool dirty,
        ref ID3D12Resource? texture,
        ref ResourceStates state,
        CpuDescriptorHandle cpuHandle)
    {
        if (!dirty)
            return;

        if (texture is null)
        {
            texture = _textureUploader.UploadRgbaTexture(
                _commandList,
                DebugTextOverlay.Width,
                DebugTextOverlay.Height,
                overlay.Rgba,
                _uploadResources);
            _textureUploader.CreateShaderResourceView(texture, cpuHandle);
            state = ResourceStates.PixelShaderResource;
        }
        else
        {
            state = _textureUploader.UpdateRgbaTexture(
                _commandList,
                texture,
                DebugTextOverlay.Width,
                DebugTextOverlay.Height,
                overlay.Rgba,
                state,
                _uploadResources);
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

        RecordOverlay(
            _controlsOverlayTexture,
            _controlsOverlayState,
            _controlsOverlayGpuHandle,
            ControlsOverlayX,
            Math.Max(DebugOverlayY, renderHeight - DebugTextOverlay.Height - ControlsOverlayBottomMargin),
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

        var constants = stackalloc float[12];
        constants[0] = x;
        constants[1] = y;
        constants[2] = Math.Min(DebugTextOverlay.Width, Math.Max(0, renderWidth - x - DebugOverlayX));
        constants[3] = Math.Min(DebugTextOverlay.Height, Math.Max(0, renderHeight - y - DebugOverlayY));
        constants[4] = renderWidth;
        constants[5] = renderHeight;
        constants[6] = 0.0f;
        constants[7] = 0.0f;
        constants[8] = uiPaperWhiteNits;
        constants[9] = uiPaperWhiteNits;
        constants[10] = 1.0f;
        constants[11] = 0.0f;

        if (constants[2] <= 0.0f || constants[3] <= 0.0f)
            return;

        _commandList.SetGraphicsRoot32BitConstants(0, 12, constants, 0);
        _commandList.SetGraphicsRootDescriptorTable(1, gpuHandle);
        _commandList.DrawInstanced(6, 1, 0, 0);
    }

    private void UpdateDebugInfo(
        SacredCamera camera,
        VisibleWorld world,
        SceneState scene,
        Dx12DebugOverlayStats rendererStats)
    {
        _framesSinceTitleUpdate++;
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

        var stats = _terrain.LastStats;
        var lines = new[]
        {
            $"FPS {_fps:0.0}",
            $"PACING {rendererStats.FramePacingStatus}",
            $"SECTORS VISIBLE {stats.VisibleSectors} LOADING {world.LoadingSectors}",
            $"GPU SECTORS {rendererStats.GpuSectorTextureCount}/{rendererStats.MaxSectorTextureCount} UPLOADING {rendererStats.PendingSectorUploadCount}",
            $"IMAGES {stats.SectorImagesDrawn}/{stats.SectorImagesCached} BUILDING {stats.SectorImagesPending}",
            $"GROUND {stats.DrawnTiles}/{stats.CandidateTiles} MISSING {stats.MissingTiles} CACHE {stats.CachedTiles}",
            $"FLOOR {stats.FloorDrawnTiles}/{stats.FloorCandidateTiles} CACHE {stats.FloorCachedTiles}",
            $"LIQUID {stats.LiquidDrawnTiles}/{stats.LiquidCandidateTiles} CACHE {stats.LiquidCachedTiles}",
            $"STATIC {stats.StaticDrawnObjects}/{stats.StaticCandidateObjects} MISSING {stats.StaticMissingObjects}",
            $"MODEL {FormatActiveModel(scene)}",
            $"CAMERA {camera.WorldCenter.X:0.0},{camera.WorldCenter.Y:0.0} SECTOR {world.CenterSector.X},{world.CenterSector.Y}"
        };

        _debugOverlay.SetLines(lines);
        _debugOverlayDirty = true;
    }

    private static string FormatActiveModel(SceneState scene)
    {
        if (scene.Models.Count == 0)
            return "NONE";

        var model = scene.Models[0];
        return $"{model.Name} V{model.Mesh.Vertices.Length} I{model.Mesh.Indices.Length}";
    }

    private void DisposeUploadResources()
    {
        foreach (var resource in _uploadResources)
            resource.Dispose();
        _uploadResources.Clear();
    }

    public void Dispose()
    {
        DisposeUploadResources();
        _debugOverlayTexture?.Dispose();
        _debugOverlayTexture = null;
        _controlsOverlayTexture?.Dispose();
        _controlsOverlayTexture = null;
        _fonts.Dispose();
    }
}

public readonly record struct Dx12DebugOverlayStats(
    int GpuSectorTextureCount,
    int MaxSectorTextureCount,
    int PendingSectorUploadCount,
    string FramePacingStatus);
