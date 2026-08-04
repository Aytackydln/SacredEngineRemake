using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.Engine.Extern;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.Models;
using Sacred.Engine.Graphics.Sprites;
using Sacred.Engine.Graphics.Swapchain;
using Sacred.Engine.Graphics.Terrain;
using Sacred.Engine.Latency;
using Sacred.Engine.Platform;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Sacred.Shaders;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace Sacred.Engine.Graphics;

public sealed class Dx12Renderer : IDisposable
{
    private const int FrameCount = 2;
    private const int MaxSectorTextures = 32;
    private const int MaxSectorTextureDescriptors = MaxSectorTextures * 2;
    private const int MaxModelTextures = 128;
    private const int DebugOverlaySrvSlot = MaxSectorTextureDescriptors;
    private const int ControlsOverlaySrvSlot = DebugOverlaySrvSlot + 1;
    private const int FirstModelTextureSrvSlot = ControlsOverlaySrvSlot + 1;
    private const int FirstStaticSpriteSrvSlot = FirstModelTextureSrvSlot + MaxModelTextures;
    private const int SrvDescriptorCount = FirstStaticSpriteSrvSlot + Dx12SpritePass.MaximumTextureCount;
    private static readonly TimeSpan ResizeDebounce = TimeSpan.FromMilliseconds(150);

    private const Format DepthBufferFormat = Format.D32_Float;

    private readonly Win32Window _window;
    private readonly AssetManager _assets;
    private readonly string _gameDirectory;
    private readonly LowLatencySystem _latency;
    private readonly TerrainRenderer _terrain;
    private readonly Stack<int> _freeModelSrvSlots = new();
    private readonly ID3D12Resource[] _backBuffers = new ID3D12Resource[FrameCount];
    private readonly ID3D12CommandList[] _submittedCommandLists = new ID3D12CommandList[1];
    private readonly ID3D12DescriptorHeap[] _shaderVisibleDescriptorHeaps = new ID3D12DescriptorHeap[1];
    private readonly WorldQuadShaderConstantsUpdater _worldQuadShaderConstants = new();
    private readonly Dx12TextureUploader _textureUploader;
    private readonly Dx12SectorTextureCache _sectorTextureCache;
    private readonly Dx12ModelTextureCache _modelTextureCache;
    private readonly Dx12ModelGeometryCache _modelGeometryCache;
    private readonly Dx12ModelPass _modelPass;
    private readonly Dx12SpritePass _spritePass;
    private readonly Action _shaderReloadHandler;

    private Dx12DebugOverlay _debugOverlay = null!;
    private IDXGIFactory2 _factory = null!;
    private ID3D12Device _device = null!;
    private ID3D12CommandQueue _commandQueue = null!;
    private Dx12SwapChain _swapChain = null!;
    private ID3D12DescriptorHeap _rtvHeap = null!;
    private ID3D12DescriptorHeap _dsvHeap = null!;
    private ID3D12DescriptorHeap _srvHeap = null!;
    private ID3D12GraphicsCommandList _commandList = null!;
    private ID3D12Fence _fence = null!;
    private ID3D12RootSignature _rootSignature = null!;
    private ID3D12PipelineState _pipelineState = null!;
    private ID3D12PipelineState _terrainLiquidCoverPipelineState = null!;
    private ID3D12Resource? _depthBuffer;
    private Dx12FrameContext[] _frameContexts = null!;
    private Dx12FrameContext? _currentFrame;

    private nint _fenceEvent;
    private int _rtvDescriptorSize;
    private int _srvDescriptorSize;
    private int _renderWidth;
    private int _renderHeight;
    private int _pendingRenderWidth;
    private int _pendingRenderHeight;
    private ulong _fenceValue;
    private Dx12SwapChainMode _requestedSwapChainMode = Dx12SwapChainMode.Sdr;
    private SwapChainFlags _swapChainFlags;
    private bool _allowTearing;
    private int _shaderReloadPending;
    private long _lastResizeRequestTimestamp;

    public Dx12Renderer(Win32Window window, AssetManager assets, string gameDirectory, LowLatencySystem latency)
    {
        _window = window;
        _assets = assets;
        _gameDirectory = gameDirectory;
        _latency = latency;
        _terrain = new TerrainRenderer(assets);
        _shaderReloadHandler = RequestShaderReload;

        for (var i = FirstModelTextureSrvSlot + MaxModelTextures - 1; i >= FirstModelTextureSrvSlot; i--)
            _freeModelSrvSlots.Push(i);
        CreateDevice();
        _textureUploader = new Dx12TextureUploader(_device);
        CreateSwapChain();
        CreateDescriptorHeaps();
        CreateBackBuffers();
        CreateDepthBuffer();
        CreateCommands();
        _sectorTextureCache = new Dx12SectorTextureCache(
            _device,
            _textureUploader,
            _srvHeap,
            _srvDescriptorSize,
            MaxSectorTextures);
        _spritePass = new Dx12SpritePass(
            _device,
            _commandList,
            _textureUploader,
            _srvHeap,
            _srvDescriptorSize,
            FirstStaticSpriteSrvSlot,
            FrameCount);
        _modelTextureCache = new Dx12ModelTextureCache(
            _assets,
            _textureUploader,
            _commandList,
            _srvHeap,
            _srvDescriptorSize,
            _freeModelSrvSlots,
            MaxModelTextures);
        _modelGeometryCache = new Dx12ModelGeometryCache(_textureUploader, FrameCount);
        _modelPass = new Dx12ModelPass(
            _commandList,
            _modelGeometryCache,
            _modelTextureCache,
            _srvHeap,
            _srvDescriptorSize,
            DebugOverlaySrvSlot);
        CreatePipeline();
        Dx12ShaderCatalog.Reloaded += _shaderReloadHandler;
    }

    public bool VariableRefreshRateSupported => _allowTearing;

    public async ValueTask RenderFrameAsync(
        SacredCamera camera,
        VisibleWorld world,
        SceneState scene,
        bool verticalSyncEnabled,
        string framePacingStatus,
        ulong frameId,
        CancellationToken cancellationToken = default)
    {
        ReloadShadersIfRequested();
        ResizeIfNeeded();
        await BeginFrameAsync(cancellationToken);
        camera.SetViewportSize(_renderWidth, _renderHeight);

        _latency.Mark(LatencyMarker.RenderSubmitStart, frameId);
        var sectorImages = _terrain.PrepareVisibleWorld(world);
        var liquidSprites = _terrain.PrepareVisibleLiquidSprites();
        var staticSprites = _terrain.PrepareVisibleStaticSprites();
        _sectorTextureCache.PrepareFrame(sectorImages, CurrentFrame, frameId);

        CurrentFrame.CommandAllocator.Reset();
        _commandList.Reset(CurrentFrame.CommandAllocator, _pipelineState);
        _modelTextureCache.PrepareFrame(scene, CurrentFrame);
        _spritePass.PrepareTextures(
            liquidSprites,
            staticSprites,
            CurrentFrame,
            _terrain.WorldSpriteRevision);

        var modelTextureStats = _modelTextureCache.Stats;

        _debugOverlay.Update(
            camera,
            world,
            scene,
            new Dx12DebugOverlayStats(
                _sectorTextureCache.Count,
                _sectorTextureCache.MaximumTextureCount,
                _sectorTextureCache.PendingUploadCount,
                modelTextureStats.Ready,
                modelTextureStats.Loading,
                modelTextureStats.Uploading,
                modelTextureStats.Failed,
                framePacingStatus),
            CurrentFrame.TransientResources);
        RecordWorldPass(camera, sectorImages, liquidSprites, staticSprites, scene);

        _commandList.Close();
        ExecuteCommandList();
        SignalFrameFence(CurrentFrame);
        _latency.Mark(LatencyMarker.RenderSubmitEnd, frameId);
        _latency.Mark(LatencyMarker.PresentStart, frameId);
        _swapChain.Present(verticalSyncEnabled, _allowTearing);
        _latency.Mark(LatencyMarker.PresentEnd, frameId);
    }

    public bool ToggleHdr()
    {
        var nextMode = _swapChain is Dx12HdrSwapChain
            ? Dx12SwapChainMode.Sdr
            : Dx12SwapChainMode.Hdr;

        RecreateSwapChain(nextMode);
        return _swapChain is Dx12HdrSwapChain;
    }

    public void Dispose()
    {
        Dx12ShaderCatalog.Reloaded -= _shaderReloadHandler;
        _sectorTextureCache.StopWorker();
        _modelTextureCache.WaitForPendingLoads();
        WaitForGpu();

        _sectorTextureCache.Dispose();

        _debugOverlay?.Dispose();

        _depthBuffer?.Dispose();
        _depthBuffer = null;

        _modelGeometryCache.Dispose();
        _modelTextureCache.Dispose();
        _spritePass.Dispose();
        DisposeBackBuffers();
        DisposePipelineResources();
        _fence.Dispose();
        _commandList.Dispose();
        foreach (var frame in _frameContexts)
            frame.Dispose();
        _srvHeap.Dispose();
        _dsvHeap.Dispose();
        _rtvHeap.Dispose();
        _swapChain.Dispose();
        _commandQueue.Dispose();
        _device.Dispose();
        _factory.Dispose();

        if (_fenceEvent != 0)
        {
            Kernel32.CloseHandle(_fenceEvent);
            _fenceEvent = 0;
        }

    }

    private void CreateDevice()
    {
        _factory = CreateDXGIFactory2<IDXGIFactory2>(false);
        _factory.MakeWindowAssociation(_window.Hwnd, WindowAssociationFlags.IgnoreAltEnter).CheckError();
        _allowTearing = CheckTearingSupport(_factory);
        _swapChainFlags = _allowTearing ? SwapChainFlags.AllowTearing : default;

        _device = D3D12CreateDevice<ID3D12Device>(null, FeatureLevel.Level_12_2);
        _commandQueue = _device.CreateCommandQueue(CommandListType.Direct);
        _latency.AttachD3D12(_device.NativePointer, _commandQueue.NativePointer);
    }

    private static bool CheckTearingSupport(IDXGIFactory2 factory)
    {
        using var factory5 = factory.QueryInterfaceOrNull<IDXGIFactory5>();
        return factory5?.PresentAllowTearing == true;
    }

    private void CreateSwapChain()
    {
        _renderWidth = _window.ClientWidth;
        _renderHeight = _window.ClientHeight;
        _swapChain = Dx12SwapChainFactory.Create(
            _requestedSwapChainMode,
            _factory,
            _commandQueue,
            _window.Hwnd,
            _renderWidth,
            _renderHeight,
            FrameCount,
            _swapChainFlags);
        _requestedSwapChainMode = _swapChain is Dx12HdrSwapChain ? Dx12SwapChainMode.Hdr : Dx12SwapChainMode.Sdr;
    }

    private void CreateDescriptorHeaps()
    {
        _rtvHeap = CreateDescriptorHeap(DescriptorHeapType.RenderTargetView, FrameCount, DescriptorHeapFlags.None);
        _dsvHeap = CreateDescriptorHeap(DescriptorHeapType.DepthStencilView, 1, DescriptorHeapFlags.None);
        _srvHeap = CreateDescriptorHeap(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, SrvDescriptorCount, DescriptorHeapFlags.ShaderVisible);
        _shaderVisibleDescriptorHeaps[0] = _srvHeap;

        _rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        _srvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
    }

    private void CreateBackBuffers()
    {
        for (var i = 0; i < FrameCount; i++)
        {
            _backBuffers[i] = _swapChain.GetBuffer((uint)i);
            _device.CreateRenderTargetView(_backBuffers[i], null, RtvHandle(i));
        }
    }

    private void CreateDepthBuffer()
    {
        _depthBuffer?.Dispose();

        var description = new ResourceDescription(
            ResourceDimension.Texture2D,
            0,
            (ulong)Math.Max(1, _renderWidth),
            (uint)Math.Max(1, _renderHeight),
            1,
            1,
            DepthBufferFormat,
            1,
            0,
            TextureLayout.Unknown,
            ResourceFlags.AllowDepthStencil);

        var clearValue = new ClearValue(DepthBufferFormat, 1.0f, 0);
        var heapProperties = new HeapProperties(HeapType.Default, 0, 0);
        _depthBuffer = _device.CreateCommittedResource(
            heapProperties,
            HeapFlags.None,
            description,
            ResourceStates.DepthWrite,
            clearValue);

        _device.CreateDepthStencilView(_depthBuffer, null, DsvHandle());
    }

    private void CreateCommands()
    {
        _frameContexts = new Dx12FrameContext[FrameCount];
        for (var index = 0; index < _frameContexts.Length; index++)
        {
            _frameContexts[index] = new Dx12FrameContext(
                index,
                _device.CreateCommandAllocator(CommandListType.Direct));
        }

        _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(
            CommandListType.Direct,
            _frameContexts[0].CommandAllocator,
            null);
        _submittedCommandLists[0] = _commandList;
        _commandList.Close();

        _fence = _device.CreateFence(0, FenceFlags.None);
        _fenceEvent = Kernel32.CreateEventA(IntPtr.Zero, false, false, null);
        if (_fenceEvent == 0)
            throw new InvalidOperationException("Failed to create D3D12 fence event.");

        _debugOverlay = new Dx12DebugOverlay(
            _commandList,
            _textureUploader,
            _terrain,
            _gameDirectory,
            SrvCpuHandle(DebugOverlaySrvSlot),
            SrvGpuHandle(DebugOverlaySrvSlot),
            SrvCpuHandle(ControlsOverlaySrvSlot),
            SrvGpuHandle(ControlsOverlaySrvSlot));
    }

    private void CreatePipeline()
    {
        var shaders = Dx12RendererPipelineFactory.Compile(_swapChain.Shaders);
        CreatePipeline(shaders);
    }

    private void CreatePipeline(Dx12CompiledShaderSet shaders)
    {
        var terrain = Dx12RendererPipelineFactory.CreateTerrain(_device, shaders, _swapChain.BackBufferFormat);
        _rootSignature = terrain.RootSignature;
        _pipelineState = terrain.Base;
        _terrainLiquidCoverPipelineState = terrain.LiquidCover;

        var staticSprites = Dx12RendererPipelineFactory.CreateStaticSprites(
            _device, shaders, _swapChain.BackBufferFormat, DepthBufferFormat, Dx12SpritePass.MaximumTextureCount);
        _spritePass.SetPipeline(staticSprites);

        var models = Dx12RendererPipelineFactory.CreateModels(
            _device, shaders, _swapChain.BackBufferFormat, DepthBufferFormat);
        _modelPass.SetPipeline(models);
    }

    private void RequestShaderReload() => Interlocked.Exchange(ref _shaderReloadPending, 1);

    private void ReloadShadersIfRequested()
    {
        if (Interlocked.Exchange(ref _shaderReloadPending, 0) == 0)
            return;

        try
        {
            // Compile first so a shader error leaves the currently rendered pipelines intact.
            var shaders = Dx12RendererPipelineFactory.Compile(_swapChain.Shaders);

            WaitForGpu();
            DisposePipelineResources();
            CreatePipeline(shaders);
            Trace.WriteLine("Reloaded Direct3D 12 shaders after Hot Reload update.");
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"Shader reload failed; keeping the existing pipelines. {exception}");
        }
    }

    private void RecreateSwapChain(Dx12SwapChainMode requestedMode)
    {
        WaitForGpu();
        DisposePipelineResources();
        DisposeBackBuffers();
        _depthBuffer?.Dispose();
        _depthBuffer = null;
        _swapChain.Dispose();

        _requestedSwapChainMode = requestedMode;
        CreateSwapChain();
        CreateBackBuffers();
        CreateDepthBuffer();
        CreatePipeline();
    }

    private void DisposePipelineResources()
    {
        _modelPass.DisposePipeline();
        _spritePass.DisposePipeline();
        _pipelineState?.Dispose();
        _pipelineState = null!;
        _terrainLiquidCoverPipelineState?.Dispose();
        _terrainLiquidCoverPipelineState = null!;
        _rootSignature?.Dispose();
        _rootSignature = null!;
    }

    private void DisposeBackBuffers()
    {
        for (var i = 0; i < _backBuffers.Length; i++)
        {
            _backBuffers[i]?.Dispose();
            _backBuffers[i] = null!;
        }
    }

    private void ResizeIfNeeded()
    {
        var width = _window.ClientWidth;
        var height = _window.ClientHeight;
        if (width <= 0 || height <= 0)
            return;

        if (width == _renderWidth && height == _renderHeight)
        {
            _pendingRenderWidth = 0;
            _pendingRenderHeight = 0;
            return;
        }

        if (width != _pendingRenderWidth || height != _pendingRenderHeight)
        {
            _pendingRenderWidth = width;
            _pendingRenderHeight = height;
            _lastResizeRequestTimestamp = Stopwatch.GetTimestamp();
            return;
        }

        if (Stopwatch.GetElapsedTime(_lastResizeRequestTimestamp) < ResizeDebounce)
            return;

        WaitForGpu();
        DisposeBackBuffers();

        _swapChain.ResizeBuffers(FrameCount, _pendingRenderWidth, _pendingRenderHeight, _swapChainFlags);
        _renderWidth = _pendingRenderWidth;
        _renderHeight = _pendingRenderHeight;
        _pendingRenderWidth = 0;
        _pendingRenderHeight = 0;
        CreateBackBuffers();
        CreateDepthBuffer();
    }

    private unsafe void RecordWorldPass(
        SacredCamera camera,
        IReadOnlyList<TerrainSectorComposition> sectorImages,
        IReadOnlyList<TerrainLiquidSprite> liquidSprites,
        IReadOnlyList<TerrainStaticSprite> staticSprites,
        SceneState scene)
    {
        var frameIndex = _swapChain.CurrentBackBufferIndex;
        var backBuffer = _backBuffers[frameIndex];
        var rtv = RtvHandle((int)frameIndex);

        Transition(backBuffer, ResourceStates.Present, ResourceStates.RenderTarget);

        var viewport = new Viewport(0, 0, _renderWidth, _renderHeight, 0.0f, 1.0f);
        var scissor = new RawRect(0, 0, _renderWidth, _renderHeight);
        _commandList.RSSetViewports(viewport);
        _commandList.RSSetScissorRects(scissor);

        _commandList.OMSetRenderTargets(rtv, null);
        _commandList.ClearRenderTargetView(rtv, new Color4(0.0f, 0.0f, 0.0f, 1.0f));

        _commandList.SetDescriptorHeaps(1, _shaderVisibleDescriptorHeaps);
        var spriteBatch = _spritePass.PrepareInstances(
            camera,
            liquidSprites,
            staticSprites,
            CurrentFrame,
            _renderWidth,
            _renderHeight,
            _terrain.WorldSpriteRevision);

        // Terrain and sprites must use this exact transform for the whole frame. Do not
        // independently rederive it per pass: tiny rounding differences open moving seams.
        var screenTransform = IsometricProjection.CreateScreenTransform(
            camera.WorldCenter,
            camera.ViewportZoom,
            _renderWidth,
            _renderHeight);

        var constants = stackalloc float[WorldQuadShaderLayout.RootConstantsCount];
        foreach (var image in sectorImages)
        {
            if (!_sectorTextureCache.TryGet(image.Coord, out var texture))
                continue;

            var drawPosition = screenTransform.ToScreen(image.IsoX, image.IsoY);
            var drawX = drawPosition.X;
            var drawY = drawPosition.Y;
            var drawWidth = screenTransform.Scale(image.Width);
            var drawHeight = screenTransform.Scale(image.Height);

            RecordTerrainLayer(
                texture.BaseSrvSlot,
                drawX,
                drawY,
                drawWidth,
                drawHeight,
                scene.Lighting.WorldQuadAmbientIntensity,
                false,
                constants);

            if (_spritePass.TryGetLiquidRange(image.Coord, out var liquidRange))
                _spritePass.RecordLiquid(
                    liquidRange,
                    scene.Lighting.WorldQuadAmbientIntensity,
                    _swapChain.DisplayProfile.ScenePaperWhiteNits,
                    CurrentFrame,
                    _renderWidth,
                    _renderHeight);

            RecordTerrainLayer(
                texture.LiquidCoverSrvSlot,
                drawX,
                drawY,
                drawWidth,
                drawHeight,
                scene.Lighting.WorldQuadAmbientIntensity,
                true,
                constants);
        }

        var dsv = DsvHandle();
        _commandList.OMSetRenderTargets(rtv, dsv);
        _commandList.ClearDepthStencilView(dsv, ClearFlags.Depth, 1.0f, 0, 0, Array.Empty<RawRect>());
        _spritePass.RecordStatic(
            spriteBatch,
            scene.Lighting.WorldQuadAmbientIntensity,
            _swapChain.DisplayProfile.ScenePaperWhiteNits,
            CurrentFrame,
            _renderWidth,
            _renderHeight);
        _modelPass.Record(
            camera,
            scene.Models,
            scene.Lighting,
            _swapChain.DisplayProfile,
            CurrentFrame.Index);

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_pipelineState);

        _commandList.OMSetRenderTargets(rtv, null);
        _debugOverlay.RecordDebugOverlay(_renderWidth, _renderHeight, _swapChain.DisplayProfile.UiPaperWhiteNits);

        Transition(backBuffer, ResourceStates.RenderTarget, ResourceStates.Present);
    }

    private unsafe void RecordTerrainLayer(
        int srvSlot,
        float drawX,
        float drawY,
        float drawWidth,
        float drawHeight,
        float ambientIntensity,
        bool premultipliedAlpha,
        float* constants)
    {
        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(premultipliedAlpha ? _terrainLiquidCoverPipelineState : _pipelineState);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        _worldQuadShaderConstants.Write(
            constants,
            new WorldQuadShaderConstants(
                new Vector4(drawX, drawY, drawWidth, drawHeight),
                new Vector2(_renderWidth, _renderHeight),
                ambientIntensity,
                premultipliedAlpha,
                _swapChain.DisplayProfile.ScenePaperWhiteNits));

        _commandList.SetGraphicsRoot32BitConstants(
            WorldQuadShaderLayout.RootConstantsRootParameter,
            WorldQuadShaderLayout.RootConstantsCount,
            constants,
            0);
        _commandList.SetGraphicsRootDescriptorTable(WorldQuadShaderLayout.TextureRootParameter, SrvGpuHandle(srvSlot));
        _commandList.DrawInstanced(6, 1, 0, 0);
    }

    private ID3D12DescriptorHeap CreateDescriptorHeap(DescriptorHeapType type, int count, DescriptorHeapFlags flags)
    {
        var description = new DescriptorHeapDescription(type, (uint)count, flags, 0);
        return _device.CreateDescriptorHeap(in description);
    }

    private void Transition(ID3D12Resource resource, ResourceStates before, ResourceStates after)
    {
        Dx12TextureUploader.Transition(_commandList, resource, before, after);
    }

    private void ExecuteCommandList()
    {
        _commandQueue.ExecuteCommandLists(1, _submittedCommandLists);
    }

    private void SignalFrameFence(Dx12FrameContext frame)
    {
        var fenceValue = ++_fenceValue;
        _commandQueue.Signal(_fence, fenceValue).CheckError();
        frame.FenceValue = fenceValue;
    }

    private void WaitForGpu()
    {
        if (_fenceValue != 0 && _fence.CompletedValue < _fenceValue)
        {
            _fence.SetEventOnCompletion(_fenceValue, _fenceEvent).CheckError();
            Kernel32.WaitForSingleObject(_fenceEvent, uint.MaxValue);
        }

        foreach (var frame in _frameContexts)
            _sectorTextureCache.OnFrameRetired(
                frame.ReleaseRetiredResources(_sectorTextureCache.FreeSrvSlots, _freeModelSrvSlots));
    }

    private async ValueTask BeginFrameAsync(CancellationToken cancellationToken)
    {
        var frame = _frameContexts[_swapChain.CurrentBackBufferIndex];
        var fenceValue = frame.FenceValue;
        if (fenceValue != 0 && _fence.CompletedValue < fenceValue)
        {
            _fence.SetEventOnCompletion(fenceValue, _fenceEvent).CheckError();
            await Win32EventAwaiter.WaitAsync(_fenceEvent, cancellationToken);
        }

        _sectorTextureCache.OnFrameRetired(
            frame.ReleaseRetiredResources(_sectorTextureCache.FreeSrvSlots, _freeModelSrvSlots));
        _currentFrame = frame;
    }

    private Dx12FrameContext CurrentFrame =>
        _currentFrame ?? throw new InvalidOperationException("No Direct3D frame is being recorded.");

    private CpuDescriptorHandle RtvHandle(int index) =>
        _rtvHeap.GetCPUDescriptorHandleForHeapStart() + index * _rtvDescriptorSize;

    private CpuDescriptorHandle DsvHandle() =>
        _dsvHeap.GetCPUDescriptorHandleForHeapStart();

    private CpuDescriptorHandle SrvCpuHandle(int index) =>
        _srvHeap.GetCPUDescriptorHandleForHeapStart() + index * _srvDescriptorSize;

    private GpuDescriptorHandle SrvGpuHandle(int index) =>
        _srvHeap.GetGPUDescriptorHandleForHeapStart() + index * _srvDescriptorSize;


}
