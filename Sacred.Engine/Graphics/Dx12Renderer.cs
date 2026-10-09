using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.ImGui;
using Sacred.Engine.Graphics.Lighting;
using Sacred.Engine.Graphics.Skinning;
using Sacred.Engine.Graphics.Sprites;
using Sacred.Engine.Graphics.Swapchain;
using Sacred.Engine.Latency;
using Sacred.Engine.Platform;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Particles;
using Sacred.Shaders;
using Sacred.World;
using Sacred.World.Particles;
using Vortice;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics;

/// <summary>Orchestrates scene render passes over a shared Direct3D 12 device context.</summary>
public sealed partial class Dx12Renderer : IDisposable
{
    private const float TemporalHistoryWeight = 0.35f;

    private readonly Stack<int> _unusedSectorSrvSlots = new();
    private readonly Stack<int> _unusedModelSrvSlots = new();
    private readonly Dx12DeviceContext _graphics;
    private readonly Dx12TextureUploader _textureUploader;
    private readonly Dx12ScreenPass _screenPass;
    private readonly Dx12Fsr2History _fsr2History;
    private readonly string _gameDirectory;
    private readonly Action _shaderReloadHandler;
    private readonly Action<Dx12FrameContext> _releaseRetiredResources;
    private readonly Queue<string?> _pendingScreenshotLabels = new();
    private readonly Dx12ScreenshotWriterQueue _screenshotWriter;
    private readonly InputState _input;
    private readonly DebugUiControlState _debugUiControls = new();

    private Dx12WorldPass? _worldPass;
    private ID3D12RootSignature _screenRootSignature = null!;
    private ID3D12PipelineState _screenPipeline = null!;
    private ID3D12RootSignature _upscaleRootSignature = null!;
    private ID3D12PipelineState _upscalePipeline = null!;
    private ID3D12RootSignature _rootSignature = null!;
    private ID3D12PipelineState _terrainPipeline = null!;
    private ID3D12PipelineState _terrainLiquidCoverPipeline = null!;
    private ID3D12PipelineState _shadowOverlayPipeline = null!;
    private int _shaderReloadPending;
    private Vector2? _previousCameraCenter;
    private float? _previousCameraViewportZoom;

    public Dx12Renderer(
        Win32Window window,
        string gameDirectory,
        LowLatencySystem latency,
        bool hdrEnabled = false,
        HdrBrightnessSettings? hdrBrightnessSettings = null,
        int renderResolutionPercentage = 100,
        RenderScalingMode renderScalingMode = RenderScalingMode.Fsr2Lanczos2,
        bool autoRenderResolution = true,
        int autoRenderResolutionMinimumPercentage = 75,
        int autoRenderResolutionMaximumPercentage = TileResolutionScaling.MaximumPercentage,
        bool autoRenderResolutionStepSnapping = true,
        int autoRenderResolutionStepPercentage = TileResolutionScaling.DefaultStepPercentage)
    {
        _gameDirectory = gameDirectory;
        _screenshotWriter = new Dx12ScreenshotWriterQueue(gameDirectory);
        _input = window.Input;
        _shaderReloadHandler = RequestShaderReload;
        _releaseRetiredResources = ReleaseRetiredResources;
        _graphics = new Dx12DeviceContext(
            window,
            latency,
            Dx12DescriptorLayout.TotalCount,
            hdrEnabled,
            hdrBrightnessSettings ?? HdrBrightnessSettings.Default);
        _graphics.OutputResized += HandleOutputResize;
        _textureUploader = new Dx12TextureUploader(_graphics.Device);
        FrameTiming.Animation.ReadGpuTimings = _graphics.GpuAnimationTimings.CaptureAndReset;
        _screenPass = new Dx12ScreenPass(
            _graphics.CommandList,
            _textureUploader,
            _graphics.SrvCpuHandle(Dx12DescriptorLayout.Screen),
            _graphics.SrvGpuHandle(Dx12DescriptorLayout.Screen));
        _fsr2History = new Dx12Fsr2History(
            _graphics.Device,
            _graphics.CommandList,
            _graphics.SrvCpuHandle(Dx12DescriptorLayout.Fsr2History));

        SetRenderResolutionPercentage(renderResolutionPercentage);
        SetAutoRenderResolutionRange(
            autoRenderResolutionMinimumPercentage,
            autoRenderResolutionMaximumPercentage);
        SetAutoRenderResolutionStepSnapping(
            autoRenderResolutionStepSnapping,
            autoRenderResolutionStepPercentage);
        AutoRenderResolution = autoRenderResolution;
        RenderScalingMode = renderScalingMode;
        CreatePipeline();
        Dx12ShaderCatalog.Reloaded += _shaderReloadHandler;
    }

    public bool VariableRefreshRateSupported => _graphics.VariableRefreshRateSupported;
    public bool IsHdrEnabled => _graphics.IsHdrEnabled;
    internal FrameTimingLog FrameTiming { get; } = new();
    internal double LastSlotWaitMilliseconds => _graphics.LastSlotWaitMilliseconds;
    internal double LastFenceWaitMilliseconds => _graphics.LastFenceWaitMilliseconds;
    internal double LastPresentMilliseconds => _graphics.LastPresentMilliseconds;
    internal double LastStreamingDispatchMilliseconds { get; private set; }
    public HdrBrightnessSettings HdrBrightnessSettings => _graphics.HdrBrightnessSettings;
    public void SetHdrBrightness(float frameWhiteNits) =>
        _graphics.SetHdrBrightnessSettings(_graphics.HdrBrightnessSettings with { SceneBrightnessNits = frameWhiteNits });
    public int OutputWidth => _graphics.OutputWidth;
    public int OutputHeight => _graphics.OutputHeight;
    public int RenderWidth => _graphics.RenderWidth;
    public int RenderHeight => _graphics.RenderHeight;
    public float RenderResolutionPercentage => AutoRenderResolution || _graphics.IsOffscreen
        ? _graphics.RenderResolutionPercentage : _manualResolutionPercentage / 100.0f;
    public bool AutoRenderResolution { get; private set; }
    public int AutoRenderResolutionMinimumPercentage { get; private set; }
    public int AutoRenderResolutionMaximumPercentage { get; private set; }
    public bool AutoRenderResolutionStepSnapping { get; private set; }
    public int AutoRenderResolutionStepPercentage { get; private set; }
    public RenderScalingMode RenderScalingMode { get; private set; }
    internal DebugUiControlState DebugUiControls => _debugUiControls;
    public bool WorldInitialized => _worldPass is not null;
    public void SetSkinPreparation(bool enabled) => GetWorldPass().SkinPreparationEnabled = enabled;
    public IParticleGpuBackend ParticleGpuBackend => GetWorldPass().ParticleGpuBackend;
    public IWorldParticleEmissionBackend WorldEmissionBackend => GetWorldPass().WorldEmissionBackend;
    public void SetWorldEmission(bool enabled) => GetWorldPass().SetWorldEmission(enabled);
    public string ParticleGpuStatus => _worldPass?.ParticleGpuStatus ?? "world not initialized";
    public string SkinningStatus => _worldPass?.SkinningStatus ?? "world not initialized";
    public SkinningMode SkinningMode => _worldPass?.SkinningMode ?? SkinningMode.Auto;
    public void SetSkinningMode(SkinningMode mode) => GetWorldPass().SkinningMode = mode;
    public void SetGpuSkinning(bool enabled) => SetSkinningMode(enabled ? SkinningMode.Gpu : SkinningMode.Cpu);
    public void SetGpuPosePlayback(bool enabled) => GetWorldPass().GpuPosePlaybackEnabled = enabled;
    public string GpuPoseStatus => _worldPass?.GpuPoseStatus ?? "world not initialized";
    public WorldPreparationStatus LastWorldPreparationStatus =>
        _worldPass?.LastPreparationStatus ?? WorldPreparationStatus.NotStarted;

    public Task StartWorldPreparation() => GetWorldPass().StartPreparation();

    public bool WaitForSectorGpuUploads
    {
        get => GetWorldPass().WaitForSectorGpuUploads;
        set => GetWorldPass().WaitForSectorGpuUploads = value;
    }

    public bool RetainUnusedSectorTextures
    {
        get => GetWorldPass().RetainUnusedSectorTextures;
        set => GetWorldPass().RetainUnusedSectorTextures = value;
    }

    public void QueueScreenshot(string? label) => _pendingScreenshotLabels.Enqueue(label);

    public void SetRenderScalingMode(RenderScalingMode mode)
    {
        if (RenderScalingMode == mode)
            return;

        RenderScalingMode = mode;
        _fsr2History.Reset();
    }

    /// <summary>Converts client-space pointer coordinates to the scene target's pixel space.</summary>
    public Vector2 OutputToRender(Vector2 position) => new(
        position.X * _graphics.RenderWidth / Math.Max(1, _graphics.OutputWidth),
        position.Y * _graphics.RenderHeight / Math.Max(1, _graphics.OutputHeight));

    public void InitializeWorld(AssetManager assets, SacredWorldArchive worldArchive)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(worldArchive);
        if (_worldPass is not null)
            throw new InvalidOperationException("The renderer's world resources are already initialized.");

        _worldPass = new Dx12WorldPass(
            assets,
            worldArchive,
            _graphics,
            _textureUploader,
            _gameDirectory,
            _input,
            _debugUiControls);
        _graphics.WaitForGpu(_releaseRetiredResources);
        CreateWorldPipeline(Dx12RendererPipelineFactory.Compile(_graphics.Shaders, _graphics.IsHdrEnabled));
    }

    /// <summary>Retires all world GPU work before scene-owned archives and assets are released.</summary>
    internal void ResetWorld()
    {
        if (_worldPass is null) return;
        _worldPass.StopBackgroundWork();
        _graphics.WaitForGpu(_releaseRetiredResources);
        DisposePipelineResources();
        _worldPass.Dispose();
        _worldPass = null;
        ResetFsr2History();
        CreatePipeline();
    }

    public ValueTask RenderScreenFrameAsync(
        ScreenFrame screen,
        bool verticalSyncEnabled,
        ulong frameId,
        CancellationToken cancellationToken = default,
        WorldPreloadRequest? worldPreload = null)
    {
        ResetFsr2History();
        _worldPass?.DiscardDebugUiFrame();
        Dx12PreparedWorldFrame prepared = default;
        if (worldPreload is not null)
            prepared = GetWorldPass().Prepare(
                worldPreload.Camera,
                worldPreload.World,
                worldPreload.Scene);

        _graphics.BeginRenderSubmission(_screenPipeline);
        _screenPass.Prepare(screen, _graphics.CurrentFrame);
        if (worldPreload is not null)
            GetWorldPass().UploadPreload(worldPreload, prepared);

        RecordScreenPass();
        SubmitAndPresent(verticalSyncEnabled, frameId);
        return ValueTask.CompletedTask;
    }

    public ValueTask RenderWorldMapAsync(
        WorldMapFrame map,
        bool verticalSyncEnabled,
        ulong frameId,
        CancellationToken cancellationToken = default)
    {
        ResetFsr2History();
        var destination = new Vector4(
            _graphics.OutputWidth * 0.5f - map.Center.X * map.Zoom,
            _graphics.OutputHeight * 0.5f - map.Center.Y * map.Zoom,
            map.Map.Width * map.Zoom,
            map.Map.Height * map.Zoom);

        _graphics.BeginRenderSubmission(_screenPipeline);
        _screenPass.Prepare(map.Map, _graphics.CurrentFrame);
        GetWorldPass().PrepareWorldMap(map);
        RecordScreenPass(destination, map.Overlay);
        SubmitAndPresent(verticalSyncEnabled, frameId);
        return ValueTask.CompletedTask;
    }

    public ValueTask RenderFrameAsync(
        SacredCamera camera,
        VisibleWorld world,
        SceneState scene,
        IReadOnlyList<WorldParticle> particles,
        ulong particleRevision,
        bool verticalSyncEnabled,
        string framePacingStatus,
        ulong frameId,
        CancellationToken cancellationToken = default)
    {
        UpdateAutoRenderResolution(camera.Zoom);
        var worldPass = GetWorldPass();
        var prepared = worldPass.Prepare(camera, world, scene, particles, particleRevision);
        _graphics.BeginRenderSubmission(_terrainPipeline);
        worldPass.UploadAndRecord(
            camera,
            world,
            scene,
            prepared,
            framePacingStatus,
            _rootSignature,
            _terrainPipeline,
            _terrainLiquidCoverPipeline,
            _shadowOverlayPipeline);
        if (_graphics.UsesRenderScaling)
            RecordUpscalePass(camera);
        else
            _fsr2History.Reset();
        worldPass.RecordUi(scene, _rootSignature, _terrainPipeline);
        SubmitAndPresent(verticalSyncEnabled, frameId);
        _previousCameraCenter = camera.ViewCenter;
        _previousCameraViewportZoom = camera.GetViewportZoom(_graphics.OutputHeight);
        return ValueTask.CompletedTask;
    }

    internal void PrepareFrame(CancellationToken cancellationToken)
    {
        if (!_graphics.IsOffscreen) UpdateRenderResolution();
        ReloadShadersIfRequested();
        _graphics.MeasureFrameWaits = FrameTiming.Enabled;
        _graphics.AcquireFrame(cancellationToken, _releaseRetiredResources);
    }

    internal void BeginDebugUiFrame(float deltaSeconds, double lastCompletedFrameTimeMilliseconds) =>
        _worldPass?.BeginDebugUiFrame(deltaSeconds, lastCompletedFrameTimeMilliseconds);

    public bool ToggleHdr()
    {
        RecreateSwapChain(_graphics.IsHdrEnabled ? Dx12SwapChainMode.Sdr : Dx12SwapChainMode.Hdr);
        return IsHdrEnabled;
    }

    public void Dispose()
    {
        Dx12ShaderCatalog.Reloaded -= _shaderReloadHandler;
        _worldPass?.StopBackgroundWork();
        _graphics.WaitForGpu(_releaseRetiredResources);
        _worldPass?.Dispose();
        _fsr2History.Dispose();
        _screenPass.Dispose();
        DisposePipelineResources();
        _screenshotWriter.Dispose();
        _graphics.Dispose();
    }

    private void CreatePipeline()
    {
        CreateScreenPipeline(Dx12RendererPipelineFactory.CompileScreen(_graphics.Shaders));
        CreateUpscalePipeline(Dx12RendererPipelineFactory.CompileUpscale(_graphics.Shaders, _graphics.IsHdrEnabled));
        if (_worldPass is not null)
            CreateWorldPipeline(Dx12RendererPipelineFactory.Compile(_graphics.Shaders, _graphics.IsHdrEnabled));
    }

    private void SubmitAndPresent(bool verticalSyncEnabled, ulong frameId)
    {
        var captureScreenshot = _pendingScreenshotLabels.Count > 0;
        var label = captureScreenshot ? _pendingScreenshotLabels.Dequeue() : null;
        var screenshot = _graphics.SubmitAndPresent(
            verticalSyncEnabled,
            frameId,
            captureScreenshot);
        var dispatchStart = FrameTiming.Enabled ? Stopwatch.GetTimestamp() : 0;
        _worldPass?.OnForegroundFrameSubmitted();
        LastStreamingDispatchMilliseconds = dispatchStart == 0 ? 0 :
            Stopwatch.GetElapsedTime(dispatchStart).TotalMilliseconds;
        if (screenshot is null)
            return;

        _screenshotWriter.Enqueue(screenshot, label);
    }

    private void CreateWorldPipeline(Dx12CompiledRendererPipelines shaders)
    {
        CreateTerrainPipeline(shaders.Terrain);
        var worldPass = _worldPass
                        ?? throw new InvalidOperationException("World rendering is not initialized.");
        worldPass.SetPipelines(
            Dx12RendererPipelineFactory.Create(
                _graphics.Device,
                shaders.SurfaceLightMap,
                Dx12SurfaceLightMapPass.TextureFormat),
            Dx12RendererPipelineFactory.Create(
                _graphics.Device,
                shaders.PlayerOcclusionMap,
                Dx12PlayerOcclusionMapPass.TextureFormat),
            Dx12RendererPipelineFactory.Create(
                _graphics.Device,
                shaders.StaticSprites,
                _graphics.BackBufferFormat,
                Dx12DeviceContext.DepthBufferFormat),
            Dx12RendererPipelineFactory.Create(
                _graphics.Device,
                shaders.LightHalos,
                _graphics.BackBufferFormat),
            Dx12RendererPipelineFactory.Create(
                _graphics.Device,
                shaders.Models,
                _graphics.BackBufferFormat,
                Dx12DeviceContext.DepthBufferFormat),
            Dx12RendererPipelineFactory.Create(
                _graphics.Device,
                shaders.ImGui,
                _graphics.BackBufferFormat),
            _graphics.IsHdrEnabled);
    }

    private void CreateScreenPipeline(Dx12CompiledPipelineGroup shaders)
    {
        var screen = Dx12RendererPipelineFactory.Create(
            _graphics.Device,
            shaders,
            _graphics.BackBufferFormat);
        _screenRootSignature = screen.RootSignature;
        _screenPipeline = screen[Dx12PipelineKind.Terrain];
    }

    private void CreateUpscalePipeline(Dx12CompiledPipelineGroup shaders)
    {
        var upscale = Dx12RendererPipelineFactory.Create(_graphics.Device, shaders, _graphics.BackBufferFormat);
        _upscaleRootSignature = upscale.RootSignature;
        _upscalePipeline = upscale[Dx12PipelineKind.Terrain];
    }

    private void CreateTerrainPipeline(Dx12CompiledPipelineGroup shaders)
    {
        var terrain = Dx12RendererPipelineFactory.Create(
            _graphics.Device,
            shaders,
            _graphics.BackBufferFormat);
        _rootSignature = terrain.RootSignature;
        _terrainPipeline = terrain[Dx12PipelineKind.Terrain];
        _terrainLiquidCoverPipeline = terrain[Dx12PipelineKind.TerrainLiquidCover];
        _shadowOverlayPipeline = terrain[Dx12PipelineKind.ShadowOverlay];
    }

    public void ReloadShaders() => RequestShaderReload();

    private void RequestShaderReload() => Interlocked.Exchange(ref _shaderReloadPending, 1);

    private void ReloadShadersIfRequested()
    {
        if (Interlocked.Exchange(ref _shaderReloadPending, 0) == 0)
            return;

        try
        {
            var screenShaders = Dx12RendererPipelineFactory.CompileScreen(_graphics.Shaders);
            var upscaleShaders = Dx12RendererPipelineFactory.CompileUpscale(_graphics.Shaders, _graphics.IsHdrEnabled);
            var rendererShaders = _worldPass is null
                ? null
                : Dx12RendererPipelineFactory.Compile(_graphics.Shaders, _graphics.IsHdrEnabled);
            _graphics.WaitForGpu(_releaseRetiredResources);
            _graphics.ReloadPresentationShaders();
            DisposePipelineResources();
            CreateScreenPipeline(screenShaders);
            CreateUpscalePipeline(upscaleShaders);
            _fsr2History.Reset();
            if (rendererShaders is null)
                return;

            CreateWorldPipeline(rendererShaders);
            EngineLog.WriteLine("Reloaded Direct3D 12 shaders.");
        }
        catch (Exception exception)
        {
            EngineLog.WriteLine($"Shader reload failed: {exception}");
        }
    }

    private void RecreateSwapChain(Dx12SwapChainMode requestedMode)
    {
        _graphics.WaitForGpu(_releaseRetiredResources);
        DisposePipelineResources();
        _graphics.RecreateSwapChain(requestedMode);
        CreatePipeline();
    }

    private void DisposePipelineResources()
    {
        _areaDownsample?.Dispose();
        _areaDownsample = null;
        _worldPass?.DisposePipelines();
        _screenPipeline?.Dispose();
        _screenPipeline = null!;
        _screenRootSignature?.Dispose();
        _screenRootSignature = null!;
        _upscalePipeline?.Dispose();
        _upscalePipeline = null!;
        _upscaleRootSignature?.Dispose();
        _upscaleRootSignature = null!;
        _terrainPipeline?.Dispose();
        _terrainPipeline = null!;
        _terrainLiquidCoverPipeline?.Dispose();
        _terrainLiquidCoverPipeline = null!;
        _shadowOverlayPipeline?.Dispose();
        _shadowOverlayPipeline = null!;
        _rootSignature?.Dispose();
        _rootSignature = null!;
    }

    private void RecordScreenPass(
        Vector4? destinationRectangle = null,
        WorldMapOverlay? worldMapOverlay = null)
    {
        Dx12TextureUploader.Transition(
            _graphics.CommandList,
            _graphics.CurrentBackBuffer,
            ResourceStates.Present,
            ResourceStates.RenderTarget);
        _graphics.CommandList.RSSetViewports(new Viewport(
            0,
            0,
            _graphics.OutputWidth,
            _graphics.OutputHeight,
            0.0f,
            1.0f));
        _graphics.CommandList.RSSetScissorRects(new RawRect(
            0,
            0,
            _graphics.OutputWidth,
            _graphics.OutputHeight));
        _graphics.CommandList.OMSetRenderTargets(_graphics.CurrentRenderTarget, null);
        _graphics.CommandList.ClearRenderTargetView(
            _graphics.CurrentRenderTarget,
            new Color4(0.0f, 0.0f, 0.0f, 1.0f));
        _graphics.CommandList.SetDescriptorHeaps(1, _graphics.ShaderVisibleDescriptorHeaps);

        if (destinationRectangle is { } destination)
        {
            _screenPass.Record(
                _screenRootSignature,
                _screenPipeline,
                _graphics.OutputWidth,
                _graphics.OutputHeight,
                _graphics.DisplayProfile.UiWhiteScale,
                destination);
        }
        else
        {
            _screenPass.Record(
                _screenRootSignature,
                _screenPipeline,
                _graphics.OutputWidth,
                _graphics.OutputHeight,
                _graphics.DisplayProfile.UiWhiteScale);
        }

        if (worldMapOverlay is { } overlay)
            GetWorldPass().RecordWorldMap(overlay, _screenRootSignature, _screenPipeline);
        Dx12TextureUploader.Transition(
            _graphics.CommandList,
            _graphics.CurrentBackBuffer,
            ResourceStates.RenderTarget,
            ResourceStates.Present);
    }

    private void ReleaseRetiredResources(Dx12FrameContext frame)
    {
        if (_worldPass is not null)
        {
            _worldPass.ReleaseRetiredResources(frame);
            return;
        }

        frame.ReleaseRetiredResources(_unusedSectorSrvSlots, _unusedModelSrvSlots);
    }

    private Dx12WorldPass GetWorldPass() =>
        _worldPass ?? throw new InvalidOperationException(
            "World rendering was requested before its resources were initialized.");
}
