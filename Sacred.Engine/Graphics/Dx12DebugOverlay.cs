using System;
using System.Diagnostics;
using System.Collections.Generic;
using Sacred.Core.World;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics;

public unsafe class Dx12DebugOverlay : IDisposable
{
    private const int DebugOverlayX = 12;
    private const int DebugOverlayY = 12;

    private readonly DebugTextOverlay _debugOverlay = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly Dx12TextureUploader _textureUploader;
    private readonly TerrainRenderer _terrain;
    private readonly CpuDescriptorHandle _debugOverlayCpuHandle;
    private readonly GpuDescriptorHandle _debugOverlayGpuHandle;
    private readonly List<ID3D12Resource> _uploadResources;

    private bool _debugOverlayDirty = true;
    private ID3D12Resource? _debugOverlayTexture;
    private ResourceStates _debugOverlayState = ResourceStates.Common;

    private int _framesSinceTitleUpdate;
    private double _lastTitleUpdateSeconds;
    private double _fps;

    public Dx12DebugOverlay(
        ID3D12GraphicsCommandList commandList,
        Dx12TextureUploader textureUploader,
        TerrainRenderer terrain,
        CpuDescriptorHandle debugOverlayCpuHandle,
        GpuDescriptorHandle debugOverlayGpuHandle)
    {
        _commandList = commandList;
        _textureUploader = textureUploader;
        _terrain = terrain;
        _debugOverlayCpuHandle = debugOverlayCpuHandle;
        _debugOverlayGpuHandle = debugOverlayGpuHandle;
        _uploadResources = [];
    }

    public void Update(SacredCamera camera, VisibleWorld world, SceneState scene, Dx12DebugOverlayStats rendererStats)
    {
        DisposeUploadResources();
        UpdateDebugInfo(camera, world, scene, rendererStats);
        UploadDebugOverlayIfNeeded();
    }

    private void UploadDebugOverlayIfNeeded()
    {
        if (!_debugOverlayDirty)
            return;

        if (_debugOverlayTexture is null)
        {
            _debugOverlayTexture = _textureUploader.UploadRgbaTexture(
                _commandList,
                DebugTextOverlay.Width,
                DebugTextOverlay.Height,
                _debugOverlay.Rgba,
                _uploadResources);
            _textureUploader.CreateShaderResourceView(_debugOverlayTexture, _debugOverlayCpuHandle);
            _debugOverlayState = ResourceStates.PixelShaderResource;
        }
        else
        {
            _debugOverlayState = _textureUploader.UpdateRgbaTexture(
                _commandList,
                _debugOverlayTexture,
                DebugTextOverlay.Width,
                DebugTextOverlay.Height,
                _debugOverlay.Rgba,
                _debugOverlayState,
                _uploadResources);
        }

        _debugOverlayDirty = false;
    }

    public void RecordDebugOverlay(int renderWidth, int renderHeight)
    {
        if (_debugOverlayTexture is null || _debugOverlayState != ResourceStates.PixelShaderResource)
            return;

        var constants = stackalloc float[8];
        constants[0] = DebugOverlayX;
        constants[1] = DebugOverlayY;
        constants[2] = Math.Min(DebugTextOverlay.Width, Math.Max(0, renderWidth - DebugOverlayX * 2));
        constants[3] = Math.Min(DebugTextOverlay.Height, Math.Max(0, renderHeight - DebugOverlayY * 2));
        constants[4] = renderWidth;
        constants[5] = renderHeight;
        constants[6] = 0.0f;
        constants[7] = 0.0f;

        if (constants[2] <= 0.0f || constants[3] <= 0.0f)
            return;

        _commandList.SetGraphicsRoot32BitConstants(0, 8, constants, 0);
        _commandList.SetGraphicsRootDescriptorTable(1, _debugOverlayGpuHandle);
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
    }
}

public readonly record struct Dx12DebugOverlayStats(
    int GpuSectorTextureCount,
    int MaxSectorTextureCount,
    int PendingSectorUploadCount);
