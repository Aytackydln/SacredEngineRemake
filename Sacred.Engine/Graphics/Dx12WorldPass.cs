using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.ImGui;
using Sacred.Engine.Graphics.Minimap;
using Sacred.Engine.Graphics.Models;
using Sacred.Engine.Graphics.Sprites;
using Sacred.Engine.Graphics.Terrain;
using Sacred.Engine.Platform;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Shaders;
using Sacred.World;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics;

/// <summary>Owns world-specific GPU resources, preparation, pipelines, and recording passes.</summary>
internal sealed class Dx12WorldPass : IDisposable
{
    private readonly Dx12DeviceContext _graphics;
    private readonly TerrainRenderer _terrain;
    private readonly Dx12SectorTextureCache _sectorTextures;
    private readonly Dx12ModelTextureCache _modelTextures;
    private readonly Dx12ModelGeometryCache _modelGeometry;
    private readonly Dx12ModelPass _models;
    private readonly Dx12SpritePass _sprites;
    private readonly Dx12LightHaloPass _lightHalos;
    private readonly Dx12MinimapPass _minimap;
    private readonly Dx12DebugOverlay _debugOverlay;
    private readonly Dx12ImGuiRenderer _imgui;
    private readonly ImGuiDebugPanel _debugPanel;
    private readonly Dx12WorldCommandRecorder _commandRecorder;
    private readonly Stack<int> _freeModelSrvSlots = new();
    private double _lastCompletedFrameTimeMilliseconds;
    private TaskCompletionSource? _preparationCompletion;

    public Dx12WorldPass(
        AssetManager assets,
        SacredWorldArchive worldArchive,
        Dx12DeviceContext graphics,
        Dx12TextureUploader textureUploader,
        string gameDirectory,
        InputState input,
        DebugUiControlState debugUiControls)
    {
        _graphics = graphics;
        for (var slot = Dx12DescriptorLayout.FirstModelTexture + Dx12DescriptorLayout.MaximumModelTextures - 1;
             slot >= Dx12DescriptorLayout.FirstModelTexture;
             slot--)
        {
            _freeModelSrvSlots.Push(slot);
        }

        _terrain = new TerrainRenderer(assets);
        _sectorTextures = new Dx12SectorTextureCache(
            graphics.Device,
            textureUploader,
            graphics.SrvHeap,
            graphics.SrvDescriptorSize,
            Dx12DescriptorLayout.MaximumSectorTextures);
        _sprites = new Dx12SpritePass(
            graphics.Device,
            graphics.CommandList,
            textureUploader,
            graphics.SrvHeap,
            graphics.SrvDescriptorSize,
            Dx12DescriptorLayout.FirstStaticSprite,
            Dx12DeviceContext.FrameCount);
        _lightHalos = new Dx12LightHaloPass(
            graphics.Device,
            graphics.CommandList,
            textureUploader,
            graphics.SrvCpuHandle(Dx12DescriptorLayout.LightHalo),
            graphics.SrvGpuHandle(Dx12DescriptorLayout.LightHalo),
            Dx12DeviceContext.FrameCount);
        _minimap = new Dx12MinimapPass(
            graphics.CommandList,
            textureUploader,
            graphics.SrvHeap,
            graphics.SrvDescriptorSize,
            Dx12DescriptorLayout.FirstMinimap,
            Dx12DeviceContext.FrameCount,
            assets,
            coord => worldArchive.TryGetMinimapTextureName(coord, out var textureName) ? textureName : null,
            gameDirectory);
        _modelTextures = new Dx12ModelTextureCache(
            assets,
            textureUploader,
            graphics.CommandList,
            graphics.SrvHeap,
            graphics.SrvDescriptorSize,
            _freeModelSrvSlots,
            Dx12DescriptorLayout.MaximumModelTextures);
        _modelGeometry = new Dx12ModelGeometryCache(assets, textureUploader, Dx12DeviceContext.FrameCount);
        _models = new Dx12ModelPass(
            graphics.CommandList,
            _modelGeometry,
            _modelTextures,
            graphics.SrvHeap,
            graphics.SrvDescriptorSize,
            Dx12DescriptorLayout.DebugOverlay);
        _debugOverlay = new Dx12DebugOverlay(
            graphics.CommandList,
            textureUploader,
            graphics.SrvCpuHandle(Dx12DescriptorLayout.DebugOverlay),
            graphics.SrvGpuHandle(Dx12DescriptorLayout.DebugOverlay));
        _imgui = new Dx12ImGuiRenderer(
            graphics.Device,
            graphics.CommandList,
            textureUploader,
            graphics.SrvHeap,
            graphics.SrvDescriptorSize,
            Dx12DescriptorLayout.ImGuiFont,
            Dx12DeviceContext.FrameCount,
            input);
        _debugPanel = new ImGuiDebugPanel(_imgui, _terrain, graphics, debugUiControls);
        _commandRecorder = new Dx12WorldCommandRecorder(
            graphics.CommandList,
            graphics.SrvHeap,
            graphics.SrvDescriptorSize,
            _sectorTextures,
            _sprites,
            _lightHalos,
            _models,
            _debugOverlay,
            _imgui,
            _minimap);
    }

    public WorldPreparationStatus LastPreparationStatus { get; private set; } =
        WorldPreparationStatus.NotStarted;

    public Task StartPreparation()
    {
        if (_preparationCompletion is null)
        {
            EngineLog.WriteLine("World preparation started: sector loading and GPU uploads are running during game load.");
            _preparationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return _preparationCompletion.Task;
    }

    public void BeginDebugUiFrame(float deltaSeconds, double lastCompletedFrameTimeMilliseconds)
    {
        _lastCompletedFrameTimeMilliseconds = lastCompletedFrameTimeMilliseconds;
        _imgui.BeginFrame(deltaSeconds, _graphics.RenderWidth, _graphics.RenderHeight);
    }

    public void DiscardDebugUiFrame() => _imgui.DiscardFrame();

    public Dx12PreparedWorldFrame Prepare(
        SacredCamera camera,
        VisibleWorld world,
        SceneState scene)
    {
        camera.SetViewportSize(_graphics.RenderWidth, _graphics.RenderHeight);
        var prepared = new Dx12PreparedWorldFrame(
            _terrain.PrepareVisibleWorld(world, scene.Indoor.ActiveGroup),
            _terrain.PrepareVisibleLiquidSprites(),
            _terrain.PrepareVisibleStaticSprites(),
            _terrain.VisibleWorldLights);
        _sectorTextures.PrepareFrame(prepared.SectorImages, _graphics.CurrentFrame);
        return prepared;
    }

    public void UploadPreload(
        WorldPreloadRequest request,
        Dx12PreparedWorldFrame prepared)
    {
        var modelGeometryReady = _modelGeometry.Prepare(request.Scene.Models);
        _modelTextures.PrepareFrame(request.Scene, _graphics.CurrentFrame);
        _sprites.PrepareTextures(
            prepared.LiquidSprites,
            prepared.StaticSprites,
            _graphics.CurrentFrame,
            _terrain.WorldSpriteRevision);
        _lightHalos.PrepareTexture(prepared.WorldLights, _graphics.CurrentFrame);
        UpdatePreparationStatus(request.World, prepared.SectorImages, modelGeometryReady);
    }

    public void UploadAndRecord(
        SacredCamera camera,
        VisibleWorld world,
        SceneState scene,
        Dx12PreparedWorldFrame prepared,
        string framePacingStatus,
        ID3D12RootSignature terrainRootSignature,
        ID3D12PipelineState terrainPipeline,
        ID3D12PipelineState liquidCoverPipeline)
    {
        _modelGeometry.Prepare(scene.Models);
        _modelTextures.PrepareFrame(scene, _graphics.CurrentFrame);
        _sprites.PrepareTextures(
            prepared.LiquidSprites,
            prepared.StaticSprites,
            _graphics.CurrentFrame,
            _terrain.WorldSpriteRevision);
        _lightHalos.PrepareTexture(prepared.WorldLights, _graphics.CurrentFrame);
        if (scene.Minimap.IsVisible)
            _minimap.Prepare(
                camera.WorldCenter,
                scene.Minimap.DifficultyDisplayName,
                scene.Minimap.RegionDisplayName,
                _graphics.CurrentFrame);

        var modelStats = _modelTextures.Stats;
        var debugStats = new Dx12DebugOverlayStats(
            _sectorTextures.Count,
            _sectorTextures.MaximumTextureCount,
            _sectorTextures.PendingUploadCount,
            modelStats.Ready,
            modelStats.Loading,
            modelStats.Uploading,
            modelStats.Failed,
            _sprites.VisibleLiquidSpriteCount,
            _sprites.VisibleStaticSpriteCount,
            _sprites.VisibleStaticShadowCount,
            _sprites.StaticShadowDrawCallCount,
            _sprites.LegacyShadowDrawCallCount,
            _lightHalos.CandidateCount,
            _lightHalos.InstanceCount,
            _lightHalos.SurfaceLightCount,
            _lastCompletedFrameTimeMilliseconds,
            framePacingStatus);
        _debugOverlay.Update(
            camera,
            world,
            debugStats,
            _graphics.CurrentFrame.TransientResources);
        if (_imgui.IsFrameBegun)
        {
            _debugPanel.Build(
                camera,
                world,
                scene,
                debugStats,
                prepared.StaticSprites,
                prepared.WorldLights,
                _debugOverlay.FramesPerSecond,
                _graphics.RenderWidth,
                _graphics.RenderHeight);
        }
        _commandRecorder.Record(
            camera,
            prepared.SectorImages,
            prepared.LiquidSprites,
            prepared.StaticSprites,
            prepared.WorldLights,
            scene,
            _terrain.WorldSpriteRevision,
            _graphics.CurrentFrame,
            _graphics.CurrentBackBuffer,
            _graphics.CurrentRenderTarget,
            _graphics.DepthStencil,
            _graphics.ShaderVisibleDescriptorHeaps,
            terrainRootSignature,
            terrainPipeline,
            liquidCoverPipeline,
            _graphics.DisplayProfile,
            _graphics.RenderWidth,
            _graphics.RenderHeight);
    }

    public void PrepareWorldMap(WorldMapOverlay overlay)
    {
        if (overlay.MinimapVisible)
        {
            _minimap.Prepare(
                overlay.TargetWorldPosition,
                overlay.DifficultyDisplayName,
                overlay.RegionDisplayName,
                _graphics.CurrentFrame);
        }
        else if (overlay.TargetMarkerVisible)
        {
            _minimap.PrepareTargetMarker(_graphics.CurrentFrame);
        }
    }

    public void RecordWorldMap(
        WorldMapOverlay overlay,
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState pipeline)
    {
        if (overlay.MinimapVisible)
        {
            _minimap.Record(
                rootSignature,
                pipeline,
                _graphics.RenderWidth,
                _graphics.RenderHeight,
                _graphics.DisplayProfile.UiPaperWhiteNits);
        }
        if (overlay.TargetMarkerVisible)
        {
            _minimap.RecordTargetMarker(
                rootSignature,
                pipeline,
                overlay.TargetScreenPosition,
                _graphics.RenderWidth,
                _graphics.RenderHeight,
                _graphics.DisplayProfile.UiPaperWhiteNits);
        }
    }

    public void SetPipelines(
        Dx12CreatedPipelineGroup staticSprites,
        Dx12CreatedPipelineGroup lightHalos,
        Dx12CreatedPipelineGroup models,
        Dx12CreatedPipelineGroup imgui)
    {
        _sprites.SetPipeline(staticSprites);
        _lightHalos.SetPipeline(lightHalos);
        _models.SetPipeline(models);
        _imgui.SetPipeline(imgui);
    }

    public void DisposePipelines()
    {
        _models.DisposePipeline();
        _sprites.DisposePipeline();
        _lightHalos.DisposePipeline();
        _imgui.DisposePipeline();
    }

    public void ReleaseRetiredResources(Dx12FrameContext frame)
    {
        var released = frame.ReleaseRetiredResources(_sectorTextures.FreeSrvSlots, _freeModelSrvSlots);
        _sectorTextures.OnFrameRetired(released);
    }

    public void OnForegroundFrameSubmitted() =>
        _sectorTextures.OnForegroundFrameSubmitted();

    public void StopBackgroundWork()
    {
        _terrain.StopBackgroundWork();
        _sectorTextures.StopWorker();
        _modelGeometry.WaitForPendingLoads();
        _modelTextures.WaitForPendingLoads();
    }

    public void Dispose()
    {
        _sectorTextures.Dispose();
        _terrain.Dispose();
        _debugOverlay.Dispose();
        _imgui.Dispose();
        _modelGeometry.Dispose();
        _modelTextures.Dispose();
        _sprites.Dispose();
        _lightHalos.Dispose();
        _minimap.Dispose();
    }

    private void UpdatePreparationStatus(
        VisibleWorld world,
        IReadOnlyList<TerrainSectorComposition> sectorImages,
        bool modelGeometryReady)
    {
        var terrainStats = _terrain.LastStats;
        LastPreparationStatus = new WorldPreparationStatus(
            world.LoadingSectors == 0 && world.Sectors.Count > 0,
            terrainStats.SectorImagesPending == 0 && sectorImages.Count == world.Sectors.Count,
            _sectorTextures.PendingUploadCount == 0 && _sectorTextures.Count >= sectorImages.Count,
            !_terrain.HasPendingSpriteAssetRequests,
            _sprites.VisibleTexturesPrepared(_terrain.WorldSpriteRevision),
            modelGeometryReady);
        if (LastPreparationStatus.IsReady && _preparationCompletion?.TrySetResult() == true)
            EngineLog.WriteLine("World preparation completed.");
    }
}

internal readonly record struct Dx12PreparedWorldFrame(
    IReadOnlyList<TerrainSectorComposition> SectorImages,
    IReadOnlyList<TerrainLiquidSprite> LiquidSprites,
    IReadOnlyList<TerrainStaticSprite> StaticSprites,
    IReadOnlyList<TerrainWorldLight> WorldLights);
