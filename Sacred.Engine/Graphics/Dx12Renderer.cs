using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.Swapchain;
using Sacred.Engine.Latency;
using Sacred.Engine.Platform;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Shaders;
using Sacred.World;
using Vortice;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics;

/// <summary>Orchestrates scene render passes over a shared Direct3D 12 device context.</summary>
public sealed class Dx12Renderer : IDisposable
{
    private readonly Stack<int> _unusedSectorSrvSlots = new();
    private readonly Stack<int> _unusedModelSrvSlots = new();
    private readonly Dx12DeviceContext _graphics;
    private readonly Dx12TextureUploader _textureUploader;
    private readonly Dx12ScreenPass _screenPass;
    private readonly string _gameDirectory;
    private readonly Action _shaderReloadHandler;
    private readonly Action<Dx12FrameContext> _releaseRetiredResources;

    private Dx12WorldPass? _worldPass;
    private ID3D12RootSignature _screenRootSignature = null!;
    private ID3D12PipelineState _screenPipeline = null!;
    private ID3D12RootSignature _rootSignature = null!;
    private ID3D12PipelineState _terrainPipeline = null!;
    private ID3D12PipelineState _terrainLiquidCoverPipeline = null!;
    private int _shaderReloadPending;

    public Dx12Renderer(
        Win32Window window,
        string gameDirectory,
        LowLatencySystem latency,
        bool hdrEnabled = false)
    {
        _gameDirectory = gameDirectory;
        _shaderReloadHandler = RequestShaderReload;
        _releaseRetiredResources = ReleaseRetiredResources;
        _graphics = new Dx12DeviceContext(
            window,
            latency,
            Dx12DescriptorLayout.TotalCount,
            hdrEnabled);
        _textureUploader = new Dx12TextureUploader(_graphics.Device);
        _screenPass = new Dx12ScreenPass(
            _graphics.CommandList,
            _textureUploader,
            _graphics.SrvCpuHandle(Dx12DescriptorLayout.Screen),
            _graphics.SrvGpuHandle(Dx12DescriptorLayout.Screen));
        CreatePipeline();
        Dx12ShaderCatalog.Reloaded += _shaderReloadHandler;
    }

    public bool VariableRefreshRateSupported => _graphics.VariableRefreshRateSupported;
    public bool IsHdrEnabled => _graphics.IsHdrEnabled;
    public bool WorldInitialized => _worldPass is not null;
    public WorldPreparationStatus LastWorldPreparationStatus =>
        _worldPass?.LastPreparationStatus ?? WorldPreparationStatus.NotStarted;

    public Task StartWorldPreparation() => GetWorldPass().StartPreparation();

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
            _gameDirectory);
        _graphics.WaitForGpu(_releaseRetiredResources);
        CreateWorldPipeline(Dx12RendererPipelineFactory.Compile(_graphics.Shaders, _graphics.IsHdrEnabled));
    }

    public ValueTask RenderScreenFrameAsync(
        ScreenFrame screen,
        bool verticalSyncEnabled,
        ulong frameId,
        CancellationToken cancellationToken = default,
        WorldPreloadRequest? worldPreload = null)
    {
        Dx12PreparedWorldFrame prepared = default;
        if (worldPreload is not null)
            prepared = GetWorldPass().Prepare(
                worldPreload.Camera,
                worldPreload.World,
                worldPreload.Scene,
                frameId);

        _graphics.BeginRenderSubmission(_screenPipeline);
        _screenPass.Prepare(screen, _graphics.CurrentFrame);
        if (worldPreload is not null)
            GetWorldPass().UploadPreload(worldPreload, prepared);

        RecordScreenPass();
        _graphics.SubmitAndPresent(verticalSyncEnabled, frameId);
        return ValueTask.CompletedTask;
    }

    public ValueTask RenderWorldMapAsync(
        WorldMapFrame map,
        bool verticalSyncEnabled,
        ulong frameId,
        CancellationToken cancellationToken = default)
    {
        var destination = new Vector4(
            _graphics.RenderWidth * 0.5f - map.Center.X * map.Zoom,
            _graphics.RenderHeight * 0.5f - map.Center.Y * map.Zoom,
            map.Map.Width * map.Zoom,
            map.Map.Height * map.Zoom);

        _graphics.BeginRenderSubmission(_screenPipeline);
        _screenPass.Prepare(map.Map, _graphics.CurrentFrame);
        GetWorldPass().PrepareWorldMap(map.Overlay);
        RecordScreenPass(destination, map.Overlay);
        _graphics.SubmitAndPresent(verticalSyncEnabled, frameId);
        return ValueTask.CompletedTask;
    }

    public ValueTask RenderFrameAsync(
        SacredCamera camera,
        VisibleWorld world,
        SceneState scene,
        bool verticalSyncEnabled,
        string framePacingStatus,
        ulong frameId,
        CancellationToken cancellationToken = default)
    {
        var worldPass = GetWorldPass();
        var prepared = worldPass.Prepare(camera, world, scene, frameId);
        _graphics.BeginRenderSubmission(_terrainPipeline);
        worldPass.UploadAndRecord(
            camera,
            world,
            scene,
            prepared,
            framePacingStatus,
            _rootSignature,
            _terrainPipeline,
            _terrainLiquidCoverPipeline);
        _graphics.SubmitAndPresent(verticalSyncEnabled, frameId);
        return ValueTask.CompletedTask;
    }

    internal void PrepareFrame(CancellationToken cancellationToken)
    {
        ReloadShadersIfRequested();
        _graphics.AcquireFrame(cancellationToken, _releaseRetiredResources);
    }

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
        _screenPass.Dispose();
        DisposePipelineResources();
        _graphics.Dispose();
    }

    private void CreatePipeline()
    {
        CreateScreenPipeline(Dx12RendererPipelineFactory.CompileScreen(_graphics.Shaders));
        if (_worldPass is not null)
            CreateWorldPipeline(Dx12RendererPipelineFactory.Compile(_graphics.Shaders, _graphics.IsHdrEnabled));
    }

    private void CreateWorldPipeline(Dx12CompiledRendererPipelines shaders)
    {
        CreateTerrainPipeline(shaders.Terrain);
        var worldPass = _worldPass
                        ?? throw new InvalidOperationException("World rendering is not initialized.");
        worldPass.SetPipelines(
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
                Dx12DeviceContext.DepthBufferFormat));
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

    private void CreateTerrainPipeline(Dx12CompiledPipelineGroup shaders)
    {
        var terrain = Dx12RendererPipelineFactory.Create(
            _graphics.Device,
            shaders,
            _graphics.BackBufferFormat);
        _rootSignature = terrain.RootSignature;
        _terrainPipeline = terrain[Dx12PipelineKind.Terrain];
        _terrainLiquidCoverPipeline = terrain[Dx12PipelineKind.TerrainLiquidCover];
    }

    private void RequestShaderReload() => Interlocked.Exchange(ref _shaderReloadPending, 1);

    private void ReloadShadersIfRequested()
    {
        if (Interlocked.Exchange(ref _shaderReloadPending, 0) == 0)
            return;

        try
        {
            var screenShaders = Dx12RendererPipelineFactory.CompileScreen(_graphics.Shaders);
            var rendererShaders = _worldPass is null
                ? null
                : Dx12RendererPipelineFactory.Compile(_graphics.Shaders, _graphics.IsHdrEnabled);
            _graphics.WaitForGpu(_releaseRetiredResources);
            DisposePipelineResources();
            CreateScreenPipeline(screenShaders);
            if (rendererShaders is null)
                return;

            CreateWorldPipeline(rendererShaders);
            Trace.WriteLine("Reloaded Direct3D 12 shaders after Hot Reload update.");
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"Shader reload failed; keeping the existing pipelines. {exception}");
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
        _worldPass?.DisposePipelines();
        _screenPipeline?.Dispose();
        _screenPipeline = null!;
        _screenRootSignature?.Dispose();
        _screenRootSignature = null!;
        _terrainPipeline?.Dispose();
        _terrainPipeline = null!;
        _terrainLiquidCoverPipeline?.Dispose();
        _terrainLiquidCoverPipeline = null!;
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
            _graphics.RenderWidth,
            _graphics.RenderHeight,
            0.0f,
            1.0f));
        _graphics.CommandList.RSSetScissorRects(new RawRect(
            0,
            0,
            _graphics.RenderWidth,
            _graphics.RenderHeight));
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
                _graphics.RenderWidth,
                _graphics.RenderHeight,
                _graphics.DisplayProfile.UiPaperWhiteNits,
                destination);
        }
        else
        {
            _screenPass.Record(
                _screenRootSignature,
                _screenPipeline,
                _graphics.RenderWidth,
                _graphics.RenderHeight,
                _graphics.DisplayProfile.UiPaperWhiteNits);
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
