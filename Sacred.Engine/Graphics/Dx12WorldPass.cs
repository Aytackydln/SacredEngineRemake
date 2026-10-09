using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.Hud;
using Sacred.Engine.Graphics.ImGui;
using Sacred.Engine.Graphics.Lighting;
using Sacred.Engine.Graphics.Minimap;
using Sacred.Engine.Graphics.Models;
using Sacred.Engine.Graphics.Particles;
using Sacred.Engine.Graphics.Skinning;
using Sacred.Engine.Graphics.Sprites;
using Sacred.Engine.Graphics.Terrain;
using Sacred.Engine.Graphics.Uploads;
using Sacred.Engine.Graphics.WorldMap;
using Sacred.Engine.Platform;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Particles;
using Sacred.Shaders;
using Sacred.World;
using Sacred.World.Particles;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics;

/// <summary>Owns world-specific GPU resources, preparation, pipelines, and recording passes.</summary>
internal sealed class Dx12WorldPass : IDisposable
{
    private readonly Dx12DeviceContext _graphics;
    private readonly TerrainRenderer _terrain;
    private readonly Dx12TextureUploadWorker _textureUploads;
    private readonly Dx12SectorTextureCache _sectorTextures;
    private readonly Dx12TerrainDebugPass _terrainDebug;
    private readonly Dx12ModelTextureCache _modelTextures;
    private readonly Dx12ModelGeometryCache _modelGeometry;
    private readonly Dx12SkinPreparationCache _skinPreparation;
    private readonly Dx12SkinDrawBindings _skinDraw;
    public bool SkinPreparationEnabled { get; set; }
    private readonly SkinningBackendSelection _skinning = new();
    private readonly GpuPoseBackendSelection _poseSelection = new();
    private string? _poseFailure;
    public bool GpuPosePlaybackEnabled
    {
        get => _poseSelection.Enabled;
        set { _poseSelection.Enabled = value; _poseFailure = null; }
    }
    public string GpuPoseStatus => $"GPU pose requested {_poseSelection.Enabled}; GPU {_poseSelection.GpuCount}, CPU {_poseSelection.CpuCount}; " +
        $"sources {_skinPreparation.Poses.SourceCount}, instances {_skinPreparation.Poses.InstanceCount}, source bytes {_skinPreparation.Poses.SourceBytes}, " +
        $"committed source bytes {_skinPreparation.Poses.SourceCommittedBytes}; output bytes {_skinPreparation.Poses.OutputBytes}, committed output bytes {_skinPreparation.Poses.OutputCommittedBytes}; " +
        $"evaluations {_skinPreparation.Poses.Evaluations}; failure {_poseFailure ?? "none"}; " +
        "player/attachments/equipment/seeds/surface queries retain CPU until phases 9-10; GPU pose visibility fail-open";
    private string? _skinningFailure;
    public SkinningMode SkinningMode { get => _skinning.Mode; set => _skinning.Mode = value; }
    private readonly Dx12ModelPass _models;
    private readonly Dx12SpritePass _sprites;
    private readonly Dx12GpuParticlePass _gpuParticles;
    public IParticleGpuBackend ParticleGpuBackend => _gpuParticles;
    public IWorldParticleEmissionBackend WorldEmissionBackend => _gpuParticles.WorldEmission;
    public void SetWorldEmission(bool enabled)
    { _gpuParticles.WorldEmission.Enabled = enabled; _gpuParticles.WorldEmission.DrawReady = _gpuParticles.IsAvailable; _gpuParticles.WorldEmission.EnsureReady(); }
    public string ParticleGpuStatus => _gpuParticles.Status;
    private readonly Dx12SurfaceLightMapPass _surfaceLights;
    private readonly Dx12ShadowMap _shadowMap;
    private readonly Dx12PlayerOcclusionMapPass _playerOcclusionMap;
    private readonly Dx12LightHaloPass _lightHalos;
    private readonly Dx12MinimapPass _minimap;
    private readonly Dx12DebugOverlay _debugOverlay;
    private readonly Dx12ImGuiRenderer _imgui;
    private readonly ImGuiDebugPanel _debugPanel;
    private readonly Dx12WorldCommandRecorder _commandRecorder;
    private readonly Dx12WorldUiPass _worldUi;
    private readonly Dx12BottomHudPass _hud;
    private readonly Dx12EscapeMenuPass _escapeMenu;
    private readonly Dx12WorldMapUiPass _mapUi;
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

        _textureUploads = new Dx12TextureUploadWorker(graphics.Device);
        _terrain = new TerrainRenderer(assets) { Portals = worldArchive.Portals };
        _terrainDebug = new Dx12TerrainDebugPass(graphics.Device, textureUploader);
        _sectorTextures = new Dx12SectorTextureCache(
            graphics.Device,
            textureUploader,
            _textureUploads,
            graphics.SrvHeap,
            graphics.SrvDescriptorSize,
            Dx12DescriptorLayout.MaximumSectorTextures,
            _terrain.InvalidateComposition);
        _surfaceLights = new Dx12SurfaceLightMapPass(
            graphics.Device,
            graphics.CommandList,
            graphics.SrvCpuHandle(Dx12DescriptorLayout.SurfaceLightMap),
            graphics.SrvGpuHandle(Dx12DescriptorLayout.SurfaceLightMap));
        _shadowMap = new Dx12ShadowMap(
            graphics.Device,
            graphics.CommandList,
            graphics.SrvCpuHandle(Dx12DescriptorLayout.ShadowMap),
            graphics.SrvGpuHandle(Dx12DescriptorLayout.ShadowMap));
        _playerOcclusionMap = new Dx12PlayerOcclusionMapPass(
            graphics.Device,
            graphics.CommandList,
            graphics.SrvCpuHandle(Dx12DescriptorLayout.PlayerOcclusionMap),
            graphics.SrvGpuHandle(Dx12DescriptorLayout.PlayerOcclusionMap));
        _sprites = new Dx12SpritePass(
            graphics.Device,
            graphics.CommandList,
            textureUploader,
            _textureUploads,
            graphics.SrvHeap,
            graphics.SrvDescriptorSize,
            Dx12DescriptorLayout.FirstStaticSprite,
            _surfaceLights.ShaderResourceHandle,
            _playerOcclusionMap.ShaderResourceHandle,
            Dx12DeviceContext.FrameCount);
        _gpuParticles = new(graphics.Device,textureUploader,assets,_sprites,() => graphics.WaitForGpu(ReleaseRetiredResources));
        _graphics.FrameSubmitted += _gpuParticles.WorldEmission.CommitSubmission;
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
            _textureUploads,
            graphics.CommandList,
            graphics.SrvHeap,
            graphics.SrvDescriptorSize,
            _freeModelSrvSlots,
            Dx12DescriptorLayout.MaximumModelTextures);
        _modelGeometry = new Dx12ModelGeometryCache(assets, graphics.Device, textureUploader, Dx12DeviceContext.FrameCount);
        _skinPreparation = new(graphics.Device, textureUploader, Dx12DeviceContext.FrameCount,
            work => assets.ScheduleVisiblePreparation(work));
        _skinDraw = new(_skinPreparation, graphics.Device, textureUploader);
        _models = new Dx12ModelPass(
            graphics.CommandList,
            _modelGeometry,
            _modelTextures,
            graphics.SrvHeap,
            graphics.SrvDescriptorSize,
            Dx12DescriptorLayout.DebugOverlay,
            Dx12DescriptorLayout.SurfaceLightMap);
        _models.GpuTimings = graphics.GpuAnimationTimings;
        _models.SkinDraw = _skinDraw;
        _debugOverlay = new Dx12DebugOverlay(
            graphics.CommandList,
            textureUploader,
            graphics.SrvCpuHandle(Dx12DescriptorLayout.DebugOverlay),
            graphics.SrvGpuHandle(Dx12DescriptorLayout.DebugOverlay),
            graphics.SrvCpuHandle(Dx12DescriptorLayout.DebugSceneDim),
            graphics.SrvGpuHandle(Dx12DescriptorLayout.DebugSceneDim));
        _imgui = new Dx12ImGuiRenderer(
            graphics.Device,
            graphics.CommandList,
            textureUploader,
            graphics.SrvHeap,
            graphics.SrvDescriptorSize,
            Dx12DescriptorLayout.ImGuiFont,
            Dx12DeviceContext.FrameCount,
            input,
            gameDirectory);
        _debugPanel = new ImGuiDebugPanel(_imgui, _terrain, assets, graphics, debugUiControls);
        _commandRecorder = new Dx12WorldCommandRecorder(
            graphics.CommandList,
            graphics.SrvHeap,
            graphics.SrvDescriptorSize,
            _sectorTextures,
            _terrainDebug,
            _sprites,
            _surfaceLights,
            _shadowMap,
            _playerOcclusionMap,
            _lightHalos,
            _models,
            _debugOverlay,
            _imgui,
            _minimap);
        _commandRecorder.GpuParticles = _gpuParticles;
        _hud = new Dx12BottomHudPass(assets, gameDirectory, graphics, textureUploader);
        _escapeMenu = new Dx12EscapeMenuPass(assets, gameDirectory, graphics, textureUploader);
        _mapUi = new Dx12WorldMapUiPass(assets, gameDirectory, graphics, textureUploader);
        _worldUi = new Dx12WorldUiPass(graphics, _commandRecorder, _minimap, _debugOverlay, _imgui, _debugPanel, _hud, _escapeMenu, _mapUi);
    }

    public string SkinningStatus => $"mode {SkinningMode}; animated models GPU {_skinning.GpuModelCount}, CPU {_skinning.CpuModelCount}, skipped {_skinning.SkippedModelCount}; pipelines {(_skinDraw.IsReady ? "ready" : "unavailable")}; " +
        $"GPU sources {_skinPreparation.SourceCount}, instances {_skinPreparation.InstanceCount}, source bytes {_skinPreparation.SourceBytes}, palette bytes {_skinPreparation.PaletteBytes}; " +
        $"committed source bytes {_skinPreparation.SourceCommittedBytes}, committed palette bytes {_skinPreparation.PaletteCommittedBytes}; " +
        $"CPU/static/equipment geometry bytes {_modelGeometry.ResidentBytes}, committed geometry bytes {_modelGeometry.CommittedBytes}; failure {_skinningFailure ?? "none"}";

    public WorldPreparationStatus LastPreparationStatus { get; private set; } =
        WorldPreparationStatus.NotStarted;

    public void EnableBulkUploads()
    {
        _textureUploads.EnableBulkUploads();
        _modelTextures.MaxConcurrentLoads = 8;
    }

    public void SetAnimationTime(float seconds)
    {
        _sprites.SetAnimationTime(seconds);
        _models.AnimationTimeOverride = float.IsFinite(seconds) ? seconds : null;
    }

    public Task StartPreparation()
    {
        if (_preparationCompletion is null)
        {
            EngineLog.WriteLine("World preparation started: sector loading and GPU uploads are running during game load.");
            _preparationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return _preparationCompletion.Task;
    }

    public bool WaitForSectorGpuUploads { get; set; }
    public bool RetainUnusedSectorTextures
    {
        get => _sectorTextures.RetainUnusedSectors;
        set
        {
            _sectorTextures.RetainUnusedSectors = value;
            _terrain.RetainUnusedSectors = value;
        }
    }

    public void BeginDebugUiFrame(float deltaSeconds, double lastCompletedFrameTimeMilliseconds)
    {
        _lastCompletedFrameTimeMilliseconds = lastCompletedFrameTimeMilliseconds;
        _imgui.BeginFrame(deltaSeconds, _graphics.OutputWidth, _graphics.OutputHeight);
    }

    public void DiscardDebugUiFrame() => _imgui.DiscardFrame();

    public Dx12PreparedWorldFrame Prepare(
        SacredCamera camera,
        VisibleWorld world,
        SceneState scene,
        IReadOnlyList<WorldParticle>? particles = null,
        ulong particleRevision = 0)
    {
        camera.SetViewportSize(_graphics.RenderWidth, _graphics.RenderHeight);
        scene.CpuParticleInputs = particles ?? Array.Empty<WorldParticle>();
        _terrain.FootprintSprites = scene.FootprintSprites;
        _terrain.FootprintRevision = scene.FootprintRevision;
        var prepared = new Dx12PreparedWorldFrame(
            _terrain.PrepareVisibleWorld(world, scene.Indoor.ActiveGroup),
            _terrain.PrepareVisibleLiquidSprites(),
            _terrain.PrepareVisibleStaticSprites(scene.GpuParticlesEnabled ? Array.Empty<WorldParticle>() : particles, particleRevision),
            _terrain.VisibleWorldLights);
        _terrain.PreparePreloadedWorld(world);
        _sectorTextures.PrepareFrame(
            prepared.SectorImages,
            _terrain.PreloadedSectorImages,
            camera.WorldCenter,
            camera.CameraSpeedUnitVector,
            _graphics.CurrentFrame);
        return prepared;
    }

    public void UploadPreload(
        WorldPreloadRequest request,
        Dx12PreparedWorldFrame prepared)
    {
        var modelGeometryReady = PrepareModels(request.Scene);
        _modelTextures.PrepareFrame(request.Scene, _graphics.CurrentFrame);
        _sprites.PrepareTextures(
            prepared.LiquidSprites,
            prepared.StaticSprites,
            _graphics.CurrentFrame,
            _terrain.WorldSpriteRevision);
        _lightHalos.PrepareTexture(prepared.WorldLights, _graphics.CurrentFrame);
        var modelStats = _modelTextures.Stats;
        UpdatePreparationStatus(request.World, prepared.SectorImages,
            modelGeometryReady && modelStats.Loading == 0 && modelStats.Uploading == 0);
    }

    public void UploadAndRecord(
        SacredCamera camera,
        VisibleWorld world,
        SceneState scene,
        Dx12PreparedWorldFrame prepared,
        string framePacingStatus,
        ID3D12RootSignature terrainRootSignature,
        ID3D12PipelineState terrainPipeline,
        ID3D12PipelineState liquidCoverPipeline,
        ID3D12PipelineState shadowOverlayPipeline)
    {
        var modelGeometryReady = PrepareModels(scene);
        _modelTextures.PrepareFrame(scene, _graphics.CurrentFrame);
        _sprites.PrepareTextures(
            prepared.LiquidSprites,
            prepared.StaticSprites,
            _graphics.CurrentFrame,
            _terrain.WorldSpriteRevision);
        _lightHalos.PrepareTexture(prepared.WorldLights, _graphics.CurrentFrame);
        _gpuParticles.WorldEmitters=scene.GpuWorldEmitters;
        _gpuParticles.Prepare(_graphics.CommandList,scene.GpuParticleBatches,scene.CpuParticleInputs,scene.GpuParticlesEnabled,
            camera,_graphics.CurrentFrame,_graphics.RenderWidth,_graphics.RenderHeight,_graphics.GpuAnimationTimings);
        if (scene.Minimap.IsVisible)
            _minimap.Prepare(
                camera.WorldCenter,
                scene.Minimap.DifficultyDisplayName,
                scene.Minimap.RegionDisplayName,
                _graphics.RenderWidth,
                _graphics.RenderHeight,
                _graphics.CurrentFrame);

        var modelStats = _modelTextures.Stats;
        UpdatePreparationStatus(world, prepared.SectorImages,
            modelGeometryReady && modelStats.Loading == 0 && modelStats.Uploading == 0);
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
        _worldUi.UpdateDebug(camera, world, scene, prepared, debugStats);
        _commandRecorder.Record(
            camera,
            prepared.SectorImages,
            prepared.LiquidSprites,
            prepared.StaticSprites,
            prepared.WorldLights,
            scene,
            _terrain.WorldSpriteRevision,
            _graphics.CurrentFrame,
            _graphics.SceneColor,
            _graphics.SceneRenderTarget,
            _graphics.SceneColorInitialState,
            _graphics.SceneColorFinalState,
            _graphics.DepthStencil,
            _graphics.ShaderVisibleDescriptorHeaps,
            terrainRootSignature,
            terrainPipeline,
            liquidCoverPipeline,
            shadowOverlayPipeline,
            _graphics.DisplayProfile,
            _graphics.RenderWidth,
            _graphics.RenderHeight);
    }

    public void PrepareWorldMap(WorldMapFrame map) => _worldUi.PrepareWorldMap(map);

    public void RecordUi(SceneState scene, ID3D12RootSignature rootSignature, ID3D12PipelineState terrainPipeline) =>
        _worldUi.RecordUi(scene, rootSignature, terrainPipeline);

    public void RecordWorldMap(WorldMapOverlay overlay, ID3D12RootSignature rootSignature, ID3D12PipelineState pipeline) =>
        _worldUi.RecordWorldMap(overlay, rootSignature, pipeline);

    public void SetPipelines(
        Dx12CreatedPipelineGroup surfaceLights,
        Dx12CreatedPipelineGroup playerOcclusionMap,
        Dx12CreatedPipelineGroup staticSprites,
        Dx12CreatedPipelineGroup lightHalos,
        Dx12CreatedPipelineGroup models,
        Dx12CreatedPipelineGroup imgui,
        bool hdrOutput)
    {
        _terrainDebug.SetHdrOutput(hdrOutput);
        _surfaceLights.SetPipeline(surfaceLights);
        _playerOcclusionMap.SetPipeline(playerOcclusionMap);
        _sprites.SetPipeline(staticSprites, hdrOutput);
        _lightHalos.SetPipeline(lightHalos);
        _models.SetPipeline(models, hdrOutput);
        try
        {
            _skinDraw.CreatePipelines(hdrOutput, _graphics.BackBufferFormat, Dx12DeviceContext.DepthBufferFormat);
            _skinningFailure = null;
            EngineLog.WriteLine("GPU skinning pipeline gate passed: all skeletal variants created for the active device/output.");
        }
        catch (Exception error)
        {
            _skinDraw.DisposePipelines();
            _skinningFailure = $"skeletal pipeline creation failed: {error.Message}";
            EngineLog.WriteLine($"Skinning fallback: {_skinningFailure}");
        }
        _gpuParticles.CreatePipelines(hdrOutput,_graphics.BackBufferFormat,Dx12DeviceContext.DepthBufferFormat);
        _imgui.SetPipeline(imgui);
    }

    public void DisposePipelines()
    {
        _terrainDebug.DisposePipeline();
        _models.DisposePipeline();
        _skinDraw.DisposePipelines();
        _skinPreparation.Poses.DisposePipelines();
        _poseFailure = null;
        _gpuParticles.DisposePipelines();
        _sprites.DisposePipeline();
        _lightHalos.DisposePipeline();
        _surfaceLights.DisposePipeline();
        _playerOcclusionMap.DisposePipeline();
        _imgui.DisposePipeline();
    }

    public void ReleaseRetiredResources(Dx12FrameContext frame)
    {
        var released = frame.ReleaseRetiredResources(_sectorTextures.FreeSrvSlots, _freeModelSrvSlots);
        _sectorTextures.OnFrameRetired(released);
    }

    public void OnForegroundFrameSubmitted()
    {
        _terrain.OnForegroundFrameSubmitted();
        _textureUploads.OnForegroundFrameSubmitted();
        _sectorTextures.OnForegroundFrameSubmitted();
    }

    public void StopBackgroundWork()
    {
        _terrain.StopBackgroundWork();
        _sectorTextures.StopWorker();
        _modelGeometry.WaitForPendingLoads();
        _skinPreparation.WaitForPendingLoads();
        _modelTextures.WaitForPendingLoads();
        _textureUploads.Stop();
    }

    public void Dispose()
    {
        _textureUploads.Stop();
        _terrainDebug.Dispose();
        _sectorTextures.Dispose();
        _terrain.Dispose();
        _debugOverlay.Dispose();
        _imgui.Dispose();
        _hud.Dispose();
        _escapeMenu.Dispose();
        _mapUi.Dispose();
        _modelGeometry.Dispose();
        _skinPreparation.Dispose();
        _skinDraw.Dispose();
        _graphics.FrameSubmitted -= _gpuParticles.WorldEmission.CommitSubmission;
        _gpuParticles.Dispose();
        _modelTextures.Dispose();
        _sprites.Dispose();
        _surfaceLights.Dispose();
        _shadowMap.Dispose();
        _playerOcclusionMap.Dispose();
        _lightHalos.Dispose();
        _minimap.Dispose();
        _textureUploads.Dispose();
    }

    private bool PrepareModels(SceneState scene)
    {
        var prepareGpu = _skinningFailure is null && _skinDraw.IsReady &&
            (SkinPreparationEnabled || SkinningMode != SkinningMode.Cpu);
        if (_poseSelection.Enabled && prepareGpu && _poseFailure is null)
        {
            try { _skinPreparation.Poses.EnsurePipelines(); }
            catch (Exception error)
            {
                _graphics.Device.DeviceRemovedReason.CheckError();
                _poseFailure = error.Message;
                EngineLog.WriteLine($"GPU pose pipeline unavailable: {_poseFailure}");
            }
        }
        _poseSelection.Apply(scene, _skinPreparation.Poses.IsReady && _poseFailure is null && prepareGpu,
            _poseFailure, SkinningMode != SkinningMode.Cpu);
        try
        {
            _skinPreparation.Prepare(prepareGpu ? scene.Models : Array.Empty<SceneModel>(), _graphics.CurrentFrame,
                _graphics.CommandList, _graphics.GpuAnimationTimings);
        }
        catch (Exception error)
        {
            _graphics.Device.DeviceRemovedReason.CheckError();
            _skinningFailure = $"GPU resource preparation failed: {error.Message}";
            EngineLog.WriteLine($"Skinning fallback: {_skinningFailure}");
        }
        _skinning.Apply(scene, _skinDraw.IsReady && _skinningFailure is null, _skinningFailure,
            geometry => _skinPreparation.TryGet(geometry, _graphics.CurrentFrame.Index, out _, out _),
            _skinPreparation.HasFailed, EngineLog.WriteLine);
        _modelGeometry.RetireUnused(scene.Models, _graphics.CurrentFrame);
        return _modelGeometry.Prepare(scene.Models);
    }

    private void UpdatePreparationStatus(
        VisibleWorld world,
        IReadOnlyList<TerrainSectorComposition> sectorImages,
        bool modelGeometryReady)
    {
        LastPreparationStatus = new WorldPreparationStatus(
            world.LoadingSectors == 0 && world.Sectors.Count > 0,
            sectorImages.Count == world.Sectors.Count,
            _sectorTextures.AreResident(sectorImages),
            !_terrain.HasPendingSpriteAssetRequests,
            _sprites.VisibleTexturesPrepared(_terrain.WorldSpriteRevision),
            modelGeometryReady);
        if (LastPreparationStatus.IsReadyForLoad(WaitForSectorGpuUploads) &&
            _preparationCompletion?.TrySetResult() == true)
            EngineLog.WriteLine($"World preparation completed; sector GPU upload waiting {(WaitForSectorGpuUploads ? "enabled" : "disabled")}; sectors resident: {LastPreparationStatus.SectorImagesUploaded}.");
    }
}

internal readonly record struct Dx12PreparedWorldFrame(
    IReadOnlyList<TerrainSectorComposition> SectorImages,
    IReadOnlyList<TerrainLiquidSprite> LiquidSprites,
    IReadOnlyList<TerrainStaticSprite> StaticSprites,
    IReadOnlyList<TerrainWorldLight> WorldLights);
