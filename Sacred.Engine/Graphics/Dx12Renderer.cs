using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets;
using Sacred.Assets.Paks.Texture;
using Sacred.Granny;
using Sacred.Core.World;
using Sacred.Engine.Assets;
using Sacred.Engine.Extern;
using Sacred.Engine.Graphics.Swapchain;
using Sacred.Engine.Platform;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace Sacred.Engine.Graphics;

public sealed class Dx12Renderer : IDisposable
{
    private const int FrameCount = 2;
    private const int MaxSectorTextures = 64;
    private const int MaxModelTextures = 32;
    private const int MaxStaticSpriteTextures = 4096;
    private const int MaxModelTextureUploadsPerFrame = 2;
    private const int MaxStaticSpriteTextureUploadsPerFrame = 8;
    private const int DebugOverlaySrvSlot = MaxSectorTextures;
    private const int ControlsOverlaySrvSlot = DebugOverlaySrvSlot + 1;
    private const int FirstModelTextureSrvSlot = ControlsOverlaySrvSlot + 1;
    private const int FirstStaticSpriteSrvSlot = FirstModelTextureSrvSlot + MaxModelTextures;
    private const int SrvDescriptorCount = FirstStaticSpriteSrvSlot + MaxStaticSpriteTextures;
    private const int IsoStepWidth = IsometricProjection.StepWidth;
    private const int IsoStepHeight = IsometricProjection.StepHeight;
    private const float PainterDepthScale = 1.0f / 4096.0f;
    private const float StaticSpriteAlphaCutoff = 0.45f;
    private const float PlayerDepthBias = 0.0005f;
    private const float ModelLocalDepthScale = 0.08f;

    private const int QuadRootConstantCount = 12;
    private static readonly int ModelVertexStride = Marshal.SizeOf<VertexPositionNormalTexture>();
    private const Format DepthBufferFormat = Format.D32_Float;
    private const SwapChainFlags SwapChainFlags = Vortice.DXGI.SwapChainFlags.AllowTearing;

    private readonly Win32Window _window;
    private readonly AssetManager _assets;
    private readonly TerrainRenderer _terrain;
    private readonly Dictionary<SectorCoord, SectorTexture> _sectorTextures = new();
    private readonly Dictionary<Mesh, ModelGpuMesh> _modelMeshes = new();
    private readonly Dictionary<string, ModelTexture> _modelTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, StaticSpriteTexture> _staticSpriteTextures = new();
    private readonly Stack<int> _freeSrvSlots = new();
    private readonly Stack<int> _freeModelSrvSlots = new();
    private readonly Stack<int> _freeStaticSpriteSrvSlots = new();
    private readonly HashSet<SectorCoord> _pendingSectorUploads = [];
    private readonly BlockingCollection<SectorUploadRequest> _sectorUploadRequests = new();
    private readonly ConcurrentQueue<CompletedSectorUpload> _completedSectorUploads = new();
    private readonly ConcurrentQueue<CompletedModelTextureLoad> _completedModelTextureLoads = new();
    private readonly List<SectorCoord> _sectorsToRemove = new(MaxSectorTextures);
    private readonly List<ID3D12Resource> _uploadResources = [];
    private readonly ID3D12Resource[] _backBuffers = new ID3D12Resource[FrameCount];
    private readonly RootConstantUploadCache _modelConstantCache = new(ModelShaderLayout.RootParameterCount);
    private readonly Dx12TextureUploader _textureUploader;

    private Dx12DebugOverlay _debugOverlay = null!;
    private IDXGIFactory2 _factory = null!;
    private ID3D12Device _device = null!;
    private ID3D12CommandQueue _commandQueue = null!;
    private ID3D12CommandQueue _sectorUploadCommandQueue = null!;
    private Dx12SwapChain _swapChain = null!;
    private ID3D12DescriptorHeap _rtvHeap = null!;
    private ID3D12DescriptorHeap _dsvHeap = null!;
    private ID3D12DescriptorHeap _srvHeap = null!;
    private ID3D12CommandAllocator _commandAllocator = null!;
    private ID3D12CommandAllocator _sectorUploadCommandAllocator = null!;
    private ID3D12GraphicsCommandList _commandList = null!;
    private ID3D12GraphicsCommandList _sectorUploadCommandList = null!;
    private ID3D12Fence _fence = null!;
    private ID3D12Fence _sectorUploadFence = null!;
    private ID3D12RootSignature _rootSignature = null!;
    private ID3D12PipelineState _pipelineState = null!;
    private ID3D12PipelineState _staticSpritePipelineState = null!;
    private ID3D12RootSignature _modelRootSignature = null!;
    private ID3D12PipelineState _modelPipelineState = null!;
    private ID3D12Resource? _depthBuffer;
    private Thread? _sectorUploadThread;

    private nint _fenceEvent;
    private nint _sectorUploadFenceEvent;
    private int _rtvDescriptorSize;
    private int _srvDescriptorSize;
    private int _renderWidth;
    private int _renderHeight;
    private ulong _fenceValue;
    private ulong _sectorUploadFenceValue;
    private bool _commandsInFlight;
    private long _releasedCpuTextureBytesSinceGc;
    private int _staticSpriteUploadsThisFrame;
    private Dx12SwapChainMode _requestedSwapChainMode = Dx12SwapChainMode.Sdr;

    public Dx12Renderer(Win32Window window, AssetManager assets)
    {
        _window = window;
        _assets = assets;
        _terrain = new TerrainRenderer(assets);

        for (var i = MaxSectorTextures - 1; i >= 0; i--)
            _freeSrvSlots.Push(i);
        for (var i = FirstModelTextureSrvSlot + MaxModelTextures - 1; i >= FirstModelTextureSrvSlot; i--)
            _freeModelSrvSlots.Push(i);
        for (var i = FirstStaticSpriteSrvSlot + MaxStaticSpriteTextures - 1; i >= FirstStaticSpriteSrvSlot; i--)
            _freeStaticSpriteSrvSlots.Push(i);

        CreateDevice();
        _textureUploader = new Dx12TextureUploader(_device);
        CreateSwapChain();
        CreateDescriptorHeaps();
        CreateBackBuffers();
        CreateDepthBuffer();
        CreateCommands();
        CreatePipeline();
    }

    public async Task RenderFrameAsync(
        SacredCamera camera,
        VisibleWorld world,
        SceneState scene,
        CancellationToken cancellationToken = default)
    {
        await WaitForGpuAsync(cancellationToken);
        DisposeUploadResources();
        ResizeIfNeeded();

        var sectorImages = _terrain.PrepareVisibleWorld(world);
        var staticSprites = _terrain.PrepareVisibleStaticSprites();
        CollectCompletedSectorUploads();
        PruneGpuSectorTextures(sectorImages);
        QueueMissingSectorUploads(sectorImages);

        _commandAllocator.Reset();
        _commandList.Reset(_commandAllocator, _pipelineState);
        _staticSpriteUploadsThisFrame = 0;
        CollectCompletedModelTextureLoads();

        _debugOverlay.Update(
            camera,
            world,
            scene,
            new Dx12DebugOverlayStats(_sectorTextures.Count, MaxSectorTextures, _pendingSectorUploads.Count));
        unsafe
        {
            RecordWorldPass(camera, sectorImages, staticSprites, scene);
        }

        _commandList.Close();
        ExecuteCommandList();
        _swapChain.Present();
        SignalFrameFence();
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
        StopSectorUploadWorker();
        WaitForGpu();
        DisposeUploadResources();

        foreach (var texture in _sectorTextures.Values)
            texture.Resource.Dispose();
        _sectorTextures.Clear();

        _debugOverlay?.Dispose();

        _depthBuffer?.Dispose();
        _depthBuffer = null;

        foreach (var mesh in _modelMeshes.Values)
            mesh.Dispose();
        _modelMeshes.Clear();

        foreach (var texture in _modelTextures.Values)
            texture.Resource?.Dispose();
        _modelTextures.Clear();
        while (_completedModelTextureLoads.TryDequeue(out _))
        {
        }

        foreach (var texture in _staticSpriteTextures.Values)
            texture.Resource.Dispose();
        _staticSpriteTextures.Clear();

        DisposeBackBuffers();
        DisposePipelineResources();
        _sectorUploadFence.Dispose();
        _fence.Dispose();
        _sectorUploadCommandList.Dispose();
        _commandList.Dispose();
        _sectorUploadCommandAllocator.Dispose();
        _commandAllocator.Dispose();
        _srvHeap.Dispose();
        _dsvHeap.Dispose();
        _rtvHeap.Dispose();
        _swapChain.Dispose();
        _sectorUploadCommandQueue.Dispose();
        _commandQueue.Dispose();
        _device.Dispose();
        _factory.Dispose();

        if (_fenceEvent != 0)
        {
            Kernel32.CloseHandle(_fenceEvent);
            _fenceEvent = 0;
        }

        if (_sectorUploadFenceEvent != 0)
        {
            Kernel32.CloseHandle(_sectorUploadFenceEvent);
            _sectorUploadFenceEvent = 0;
        }
    }

    private void CreateDevice()
    {
        _factory = CreateDXGIFactory2<IDXGIFactory2>(false);
        _factory.MakeWindowAssociation(_window.Hwnd, WindowAssociationFlags.IgnoreAltEnter).CheckError();

        _device = D3D12CreateDevice<ID3D12Device>(null, FeatureLevel.Level_12_2);
        _commandQueue = _device.CreateCommandQueue(CommandListType.Direct);
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
            SwapChainFlags);
        _requestedSwapChainMode = _swapChain is Dx12HdrSwapChain ? Dx12SwapChainMode.Hdr : Dx12SwapChainMode.Sdr;
    }

    private void CreateDescriptorHeaps()
    {
        _rtvHeap = CreateDescriptorHeap(DescriptorHeapType.RenderTargetView, FrameCount, DescriptorHeapFlags.None);
        _dsvHeap = CreateDescriptorHeap(DescriptorHeapType.DepthStencilView, 1, DescriptorHeapFlags.None);
        _srvHeap = CreateDescriptorHeap(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, SrvDescriptorCount, DescriptorHeapFlags.ShaderVisible);

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
        _commandAllocator = _device.CreateCommandAllocator(CommandListType.Direct);

        _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, _commandAllocator, null);
        _commandList.Close();

        _fence = _device.CreateFence(0, FenceFlags.None);
        _fenceEvent = Kernel32.CreateEventA(IntPtr.Zero, false, false, null);
        if (_fenceEvent == 0)
            throw new InvalidOperationException("Failed to create D3D12 fence event.");

        _sectorUploadCommandQueue = _device.CreateCommandQueue(CommandListType.Direct);
        _sectorUploadCommandAllocator = _device.CreateCommandAllocator(CommandListType.Direct);
        _sectorUploadCommandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, _sectorUploadCommandAllocator, null);
        _sectorUploadCommandList.Close();
        _sectorUploadFence = _device.CreateFence(0, FenceFlags.None);
        _sectorUploadFenceEvent = Kernel32.CreateEventA(IntPtr.Zero, false, false, null);
        if (_sectorUploadFenceEvent == 0)
            throw new InvalidOperationException("Failed to create D3D12 sector-upload fence event.");

        _sectorUploadThread = new Thread(SectorUploadWorkerLoop)
        {
            IsBackground = true,
            Name = "Sacred sector texture uploader"
        };
        _sectorUploadThread.Start();

        _debugOverlay = new Dx12DebugOverlay(
            _commandList,
            _textureUploader,
            _terrain,
            SrvCpuHandle(DebugOverlaySrvSlot),
            SrvGpuHandle(DebugOverlaySrvSlot),
            SrvCpuHandle(ControlsOverlaySrvSlot),
            SrvGpuHandle(ControlsOverlaySrvSlot));
    }

    private void CreatePipeline()
    {
        CreateTerrainPipeline();
        CreateStaticSpritePipeline();
        CreateModelPipeline();
    }

    private void CreateTerrainPipeline()
    {
        var rootParameters = new[]
        {
            new RootParameter(new RootConstants(0, 0, QuadRootConstantCount), ShaderVisibility.All),
            new RootParameter(
                new RootDescriptorTable
                {
                    Ranges = [new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0, 0, 0)]
                },
                ShaderVisibility.Pixel)
        };

        var samplers = new[]
        {
            new StaticSamplerDescription(
                0,
                Filter.MinMagMipLinear,
                TextureAddressMode.Wrap,
                TextureAddressMode.Wrap,
                TextureAddressMode.Wrap,
                0.0f,
                16,
                ComparisonFunction.Never,
                StaticBorderColor.TransparentBlack,
                0.0f,
                float.MaxValue,
                ShaderVisibility.Pixel,
                0)
        };

        var rootDescription = new RootSignatureDescription(
            RootSignatureFlags.AllowInputAssemblerInputLayout,
            rootParameters,
            samplers);

        _rootSignature = _device.CreateRootSignature(in rootDescription, RootSignatureVersion.Version1);

        var vertexShader = Dx12ShaderCompiler.CompileShader(_swapChain.Shaders.QuadWorldVertexShader);
        var pixelShader = Dx12ShaderCompiler.CompileShader(_swapChain.Shaders.QuadWorldPixelShader);

        var pipelineDescription = new GraphicsPipelineStateDescription
        {
            RootSignature = _rootSignature,
            VertexShader = vertexShader,
            PixelShader = pixelShader,
            BlendState = BlendDescription.AlphaBlend,
            RasterizerState = RasterizerDescription.CullNone,
            DepthStencilState = DepthStencilDescription.None,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [_swapChain.BackBufferFormat],
            DepthStencilFormat = Format.Unknown,
            SampleDescription = new SampleDescription(1, 0)
        };

        _pipelineState = _device.CreateGraphicsPipelineState(pipelineDescription);
    }

    private void CreateStaticSpritePipeline()
    {
        var vertexShader = Dx12ShaderCompiler.CompileShader(_swapChain.Shaders.StaticSpriteVertexShader);
        var pixelShader = Dx12ShaderCompiler.CompileShader(_swapChain.Shaders.StaticSpritePixelShader);
        var depthStencil = DepthStencilDescription.Default;
        depthStencil.DepthFunc = ComparisonFunction.LessEqual;

        var pipelineDescription = new GraphicsPipelineStateDescription
        {
            RootSignature = _rootSignature,
            VertexShader = vertexShader,
            PixelShader = pixelShader,
            BlendState = BlendDescription.AlphaBlend,
            RasterizerState = RasterizerDescription.CullNone,
            DepthStencilState = depthStencil,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [_swapChain.BackBufferFormat],
            DepthStencilFormat = DepthBufferFormat,
            SampleDescription = new SampleDescription(1, 0)
        };

        _staticSpritePipelineState = _device.CreateGraphicsPipelineState(pipelineDescription);
    }

    private void CreateModelPipeline()
    {
        var rootParameters = new[]
        {
            new RootParameter(
                new RootConstants(
                    ModelShaderLayout.ModelConstantsRegister,
                    0,
                    ModelShaderLayout.ModelConstantsCount),
                ShaderVisibility.All),
            new RootParameter(
                new RootDescriptorTable
                {
                    Ranges =
                    [
                        new DescriptorRange(
                            DescriptorRangeType.ShaderResourceView,
                            1,
                            ModelShaderLayout.ModelTextureRegister,
                            0,
                            0)
                    ]
                },
                ShaderVisibility.Pixel),
            new RootParameter(
                new RootConstants(
                    ModelShaderLayout.SceneConstantsRegister,
                    0,
                    ModelShaderLayout.SceneConstantsCount),
                ShaderVisibility.Pixel)
        };

        var samplers = new[]
        {
            new StaticSamplerDescription(
                ModelShaderLayout.ModelSamplerRegister,
                Filter.MinMagMipLinear,
                TextureAddressMode.Clamp,
                TextureAddressMode.Clamp,
                TextureAddressMode.Clamp,
                0.0f,
                16,
                ComparisonFunction.Never,
                StaticBorderColor.OpaqueWhite,
                0.0f,
                float.MaxValue,
                ShaderVisibility.Pixel,
                0)
        };

        var rootDescription = new RootSignatureDescription(
            RootSignatureFlags.AllowInputAssemblerInputLayout,
            rootParameters,
            samplers);

        _modelRootSignature = _device.CreateRootSignature(in rootDescription, RootSignatureVersion.Version1);

        var vertexShader = Dx12ShaderCompiler.CompileShader(_swapChain.Shaders.ModelVertexShader);
        var pixelShader = Dx12ShaderCompiler.CompileShader(_swapChain.Shaders.ModelPixelShader);
        var depthStencil = DepthStencilDescription.Default;
        depthStencil.DepthFunc = ComparisonFunction.LessEqual;

        var pipelineDescription = new GraphicsPipelineStateDescription
        {
            RootSignature = _modelRootSignature,
            VertexShader = vertexShader,
            PixelShader = pixelShader,
            InputLayout = new[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 24, 0)
            },
            BlendState = BlendDescription.AlphaBlend,
            RasterizerState = RasterizerDescription.CullCounterClockwise,
            DepthStencilState = depthStencil,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [_swapChain.BackBufferFormat],
            DepthStencilFormat = DepthBufferFormat,
            SampleDescription = new SampleDescription(1, 0)
        };

        _modelPipelineState = _device.CreateGraphicsPipelineState(pipelineDescription);
    }

    private void RecreateSwapChain(Dx12SwapChainMode requestedMode)
    {
        WaitForGpu();
        DisposeUploadResources();
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
        _modelPipelineState?.Dispose();
        _modelPipelineState = null!;
        _modelRootSignature?.Dispose();
        _modelRootSignature = null!;
        _staticSpritePipelineState?.Dispose();
        _staticSpritePipelineState = null!;
        _pipelineState?.Dispose();
        _pipelineState = null!;
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
        if (width == _renderWidth && height == _renderHeight)
            return;

        DisposeBackBuffers();

        _swapChain.ResizeBuffers(FrameCount, width, height, SwapChainFlags);
        _renderWidth = width;
        _renderHeight = height;
        CreateBackBuffers();
        CreateDepthBuffer();
    }

    private void CollectCompletedSectorUploads()
    {
        while (_completedSectorUploads.TryDequeue(out var upload))
        {
            _pendingSectorUploads.Remove(upload.Coord);

            if (upload.Error is not null)
            {
                _freeSrvSlots.Push(upload.SrvSlot);
                throw new InvalidOperationException($"Failed to upload sector texture {upload.Coord.X},{upload.Coord.Y}.", upload.Error);
            }

            if (upload.Resource is null)
            {
                _freeSrvSlots.Push(upload.SrvSlot);
                continue;
            }

            TrackReleasedCpuTextureBytes(upload.ReleasedCpuBytes);

            if (_sectorTextures.TryGetValue(upload.Coord, out var existing))
            {
                existing.Resource.Dispose();
                _freeSrvSlots.Push(existing.SrvSlot);
                _sectorTextures.Remove(upload.Coord);
            }

            _textureUploader.CreateShaderResourceView(upload.Resource, SrvCpuHandle(upload.SrvSlot));
            _sectorTextures.Add(upload.Coord, new SectorTexture(upload.Resource, upload.SrvSlot));
        }
    }

    private void QueueMissingSectorUploads(IReadOnlyList<TerrainSectorImage> sectorImages)
    {
        foreach (var image in sectorImages)
        {
            if (_sectorTextures.ContainsKey(image.Coord) || _pendingSectorUploads.Contains(image.Coord))
                continue;

            if (!image.HasCpuPixels)
                continue;

            if (_freeSrvSlots.Count == 0)
                return;

            var slot = _freeSrvSlots.Pop();
            _pendingSectorUploads.Add(image.Coord);

            if (!_sectorUploadRequests.TryAdd(new SectorUploadRequest(image, slot)))
            {
                _pendingSectorUploads.Remove(image.Coord);
                _freeSrvSlots.Push(slot);
                return;
            }
        }
    }

    private void CollectCompletedModelTextureLoads()
    {
        var uploadsThisFrame = 0;
        while (_completedModelTextureLoads.TryDequeue(out var completed))
        {
            if (!_modelTextures.TryGetValue(completed.TextureName, out var texture))
                continue;

            texture.Pending = false;
            if (completed.Error is not null || completed.Asset is null)
            {
                texture.Failed = true;
                continue;
            }

            if (_freeModelSrvSlots.Count == 0)
            {
                texture.Failed = true;
                continue;
            }

            try
            {
                var slot = _freeModelSrvSlots.Pop();
                texture.SrvSlot = slot;
                uploadsThisFrame++;
                var resource = _textureUploader.UploadRgbaTexture(
                    _commandList,
                    completed.Asset.Width,
                    completed.Asset.Height,
                    completed.Asset.Rgba8,
                    _uploadResources);
                _textureUploader.CreateShaderResourceView(resource, SrvCpuHandle(slot));
                texture.Resource = resource;
            }
            catch
            {
                texture.Resource?.Dispose();
                texture.Resource = null;
                if (texture.SrvSlot >= 0)
                {
                    _freeModelSrvSlots.Push(texture.SrvSlot);
                    texture.SrvSlot = -1;
                }

                texture.Failed = true;
            }

            if (uploadsThisFrame >= MaxModelTextureUploadsPerFrame)
                break;
        }
    }

    private ModelTexture? GetOrRequestModelTexture(string? textureName)
    {
        if (string.IsNullOrWhiteSpace(textureName))
            return null;

        if (_modelTextures.TryGetValue(textureName, out var existing))
            return existing.Resource is not null ? existing : null;

        var texture = new ModelTexture(textureName)
        {
            Pending = true
        };
        _modelTextures.Add(textureName, texture);

        _ = LoadModelTextureAsync(textureName);

        return null;
    }

    private async Task LoadModelTextureAsync(string textureName)
    {
        try
        {
            var asset = await _assets.LoadTextureAsync(textureName);
            _completedModelTextureLoads.Enqueue(new CompletedModelTextureLoad(textureName, asset, null));
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            _completedModelTextureLoads.Enqueue(new CompletedModelTextureLoad(textureName, null, ex));
        }
    }

    private StaticSpriteTexture? GetOrCreateStaticSpriteTexture(StaticSpriteAsset sprite)
    {
        if (_staticSpriteTextures.TryGetValue(sprite.GroupId, out var cached))
            return cached;

        if (_freeStaticSpriteSrvSlots.Count == 0)
            return null;

        if (_staticSpriteUploadsThisFrame >= MaxStaticSpriteTextureUploadsPerFrame)
            return null;

        var slot = _freeStaticSpriteSrvSlots.Pop();
        try
        {
            _staticSpriteUploadsThisFrame++;
            var resource = _textureUploader.UploadRgbaTexture(
                _commandList,
                sprite.Width,
                sprite.Height,
                sprite.Rgba,
                _uploadResources);
            _textureUploader.CreateShaderResourceView(resource, SrvCpuHandle(slot));
            var texture = new StaticSpriteTexture(resource, slot);
            _staticSpriteTextures.Add(sprite.GroupId, texture);
            return texture;
        }
        catch
        {
            _freeStaticSpriteSrvSlots.Push(slot);
            throw;
        }
    }

    private CompletedSectorUpload UploadSectorTextureOnWorker(SectorUploadRequest request)
    {
        try
        {
            var image = request.Image;
            var texture = _textureUploader.UploadRgbaTextureAndWait(
                _sectorUploadCommandQueue,
                _sectorUploadCommandAllocator,
                _sectorUploadCommandList,
                _sectorUploadFence,
                _sectorUploadFenceEvent,
                ref _sectorUploadFenceValue,
                image.Width,
                image.Height,
                image.GetCpuPixels());
            var releasedCpuBytes = image.ReleaseCpuPixels();

            return new CompletedSectorUpload(image.Coord, texture, request.SrvSlot, null, releasedCpuBytes);
        }
        catch (Exception ex)
        {
            return new CompletedSectorUpload(request.Image.Coord, null, request.SrvSlot, ex, 0);
        }
    }

    private void TrackReleasedCpuTextureBytes(int bytes)
    {
        if (bytes <= 0)
            return;

        _releasedCpuTextureBytesSinceGc += bytes;
        if (_releasedCpuTextureBytesSinceGc < 256L * 1024 * 1024)
            return;

        _releasedCpuTextureBytesSinceGc = 0;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
    }

    private unsafe void RecordWorldPass(
        SacredCamera camera,
        IReadOnlyList<TerrainSectorImage> sectorImages,
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

        _commandList.SetDescriptorHeaps([_srvHeap]);
        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_pipelineState);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        var centerIsoX = (camera.WorldCenter.X - camera.WorldCenter.Y) * (IsoStepWidth * 0.5f);
        var centerIsoY = (camera.WorldCenter.X + camera.WorldCenter.Y) * (IsoStepHeight * 0.5f);

        var constants = stackalloc float[QuadRootConstantCount];
        foreach (var image in sectorImages)
        {
            if (!_sectorTextures.TryGetValue(image.Coord, out var texture))
                continue;

            var drawX = _renderWidth * 0.5f + (image.IsoX - centerIsoX) * camera.Zoom;
            var drawY = _renderHeight * 0.5f + (image.IsoY - centerIsoY) * camera.Zoom;
            var drawWidth = image.Width * camera.Zoom;
            var drawHeight = image.Height * camera.Zoom;

            constants[0] = drawX;
            constants[1] = drawY;
            constants[2] = drawWidth;
            constants[3] = drawHeight;
            constants[4] = _renderWidth;
            constants[5] = _renderHeight;
            constants[6] = 0.0f;
            constants[7] = 0.0f;
            constants[8] = _swapChain.DisplayProfile.ScenePaperWhiteNits;
            constants[9] = _swapChain.DisplayProfile.UiPaperWhiteNits;
            constants[10] = 0.0f;
            constants[11] = 0.0f;

            _commandList.SetGraphicsRoot32BitConstants(0, QuadRootConstantCount, constants, 0);
            _commandList.SetGraphicsRootDescriptorTable(1, SrvGpuHandle(texture.SrvSlot));
            _commandList.DrawInstanced(6, 1, 0, 0);
        }

        var dsv = DsvHandle();
        _commandList.OMSetRenderTargets(rtv, dsv);
        _commandList.ClearDepthStencilView(dsv, ClearFlags.Depth, 1.0f, 0, 0, []);
        RecordStaticSpritePass(camera, staticSprites);
        RecordModelPass(camera, scene.Models, scene.Lighting);

        _commandList.OMSetRenderTargets(rtv, null);
        _debugOverlay.RecordDebugOverlay(_renderWidth, _renderHeight, _swapChain.DisplayProfile.UiPaperWhiteNits);

        Transition(backBuffer, ResourceStates.RenderTarget, ResourceStates.Present);
    }

    private unsafe void RecordStaticSpritePass(
        SacredCamera camera,
        IReadOnlyList<TerrainStaticSprite> sprites)
    {
        if (sprites.Count == 0)
            return;

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_staticSpritePipelineState);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);

        var centerIsoX = (camera.WorldCenter.X - camera.WorldCenter.Y) * (IsoStepWidth * 0.5f);
        var centerIsoY = (camera.WorldCenter.X + camera.WorldCenter.Y) * (IsoStepHeight * 0.5f);
        var constants = stackalloc float[QuadRootConstantCount];

        for (var i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
            var texture = GetOrCreateStaticSpriteTexture(sprite.Sprite);
            if (texture is null)
                continue;

            var drawX = _renderWidth * 0.5f + (sprite.IsoX - centerIsoX) * camera.Zoom;
            var drawY = _renderHeight * 0.5f + (sprite.IsoY - centerIsoY) * camera.Zoom;
            var drawWidth = sprite.Sprite.Width * camera.Zoom;
            var drawHeight = sprite.Sprite.Height * camera.Zoom;

            constants[0] = drawX;
            constants[1] = drawY;
            constants[2] = drawWidth;
            constants[3] = drawHeight;
            constants[4] = _renderWidth;
            constants[5] = _renderHeight;
            constants[6] = CalculateStaticSpriteSceneDepth(camera, sprite);
            constants[7] = StaticSpriteAlphaCutoff;
            constants[8] = _swapChain.DisplayProfile.ScenePaperWhiteNits;
            constants[9] = _swapChain.DisplayProfile.UiPaperWhiteNits;
            constants[10] = 0.0f;
            constants[11] = 0.0f;

            _commandList.SetGraphicsRoot32BitConstants(0, QuadRootConstantCount, constants, 0);
            _commandList.SetGraphicsRootDescriptorTable(1, SrvGpuHandle(texture.SrvSlot));
            _commandList.DrawInstanced(6, 1, 0, 0);
        }
    }

    private static float CalculateStaticSpriteSceneDepth(SacredCamera camera, TerrainStaticSprite sprite)
    {
        var depthKey = sprite.TileDepth
                       + sprite.TileWorldY * 0.001f
                       + sprite.TileWorldX * 0.000001f
                       + sprite.ChainDepth * 0.0000001f;

        return CalculatePainterSceneDepth(camera, depthKey);
    }

    private static float CalculateModelSceneDepth(SacredCamera camera, SceneModel model)
    {
        var depthKey = model.Position.X + model.Position.Y + model.Position.Y * 0.001f;
        return Math.Clamp(CalculatePainterSceneDepth(camera, depthKey) + PlayerDepthBias, 0.0f, 1.0f);
    }

    private static float CalculatePainterSceneDepth(SacredCamera camera, float depthKey)
    {
        var centerDepthKey = camera.WorldCenter.X + camera.WorldCenter.Y + camera.WorldCenter.Y * 0.001f;
        return Math.Clamp(0.50f - (depthKey - centerDepthKey) * PainterDepthScale, 0.20f, 0.72f);
    }

    private unsafe void RecordModelPass(SacredCamera camera, IReadOnlyList<SceneModel> models, SceneLighting lighting)
    {
        if (models.Count == 0)
            return;

        PruneUnusedModelTextures(models);

        _commandList.SetGraphicsRootSignature(_modelRootSignature);
        _commandList.SetPipelineState(_modelPipelineState);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _modelConstantCache.Reset();

        var sceneConstants = stackalloc float[ModelShaderLayout.SceneConstantsCount];
        WriteModelLighting(camera, lighting, _swapChain.DisplayProfile, sceneConstants);
        SetModelRootConstantsIfChanged(
            ModelShaderLayout.SceneConstantsRootParameter,
            sceneConstants,
            ModelShaderLayout.SceneConstantsCount,
            0);

        var constants = stackalloc float[ModelShaderLayout.ModelConstantsCount];
        foreach (var model in models)
        {
            if (model.Mesh.Vertices.Length == 0 || model.Mesh.Indices.Length == 0)
                continue;

            var mesh = GetOrCreateModelGpuMesh(model.Mesh);
            var world = model.Transform;
            var worldViewProjection = model.Transform * camera.View * camera.Projection;
            var modelSceneDepth = CalculateModelSceneDepth(camera, model);
            WriteMatrix(worldViewProjection, constants);
            WriteMatrix(world, constants + 16);
            WriteModelColor(model.Name, constants + 32);

            var vertexBufferView = mesh.VertexBufferView;
            var indexBufferView = mesh.IndexBufferView;
            _commandList.IASetVertexBuffers(0, 1, &vertexBufferView);
            _commandList.IASetIndexBuffer(&indexBufferView);
            SetModelRootConstantsIfChanged(
                ModelShaderLayout.ModelConstantsRootParameter,
                constants,
                ModelShaderLayout.ModelBaseConstantsCount,
                ModelShaderLayout.ModelBaseConstantsOffset);

            if (model.Mesh.Surfaces.Count == 0)
            {
                WriteModelTextureFlags(
                    hasTexture: false,
                    localDepthScale: ModelLocalDepthScale,
                    painterDepth: modelSceneDepth,
                    alphaCutoff: 0.0f,
                    constants + ModelShaderLayout.TextureFlagsOffset);
                SetModelRootConstantsIfChanged(
                    ModelShaderLayout.ModelConstantsRootParameter,
                    constants + ModelShaderLayout.TextureFlagsOffset,
                    ModelShaderLayout.TextureFlagsConstantsCount,
                    ModelShaderLayout.TextureFlagsOffset);
                _commandList.SetGraphicsRootDescriptorTable(
                    ModelShaderLayout.ModelTextureRootParameter,
                    SrvGpuHandle(DebugOverlaySrvSlot));
                _commandList.DrawIndexedInstanced((uint)mesh.IndexCount, 1, 0, 0, 0);
                continue;
            }

            foreach (var surface in model.Mesh.Surfaces)
            {
                if (surface.IndexCount <= 0 || surface.IndexStart >= mesh.IndexCount)
                    continue;

                var drawCount = Math.Min(surface.IndexCount, mesh.IndexCount - surface.IndexStart);
                var texture = GetOrRequestModelTexture(surface.TextureName);
                var hasTexture = texture is { Resource: not null, SrvSlot: >= 0 };
                WriteModelTextureFlags(
                    hasTexture,
                    ModelLocalDepthScale,
                    modelSceneDepth,
                    0.10f,
                    constants + ModelShaderLayout.TextureFlagsOffset);

                SetModelRootConstantsIfChanged(
                    ModelShaderLayout.ModelConstantsRootParameter,
                    constants + ModelShaderLayout.TextureFlagsOffset,
                    ModelShaderLayout.TextureFlagsConstantsCount,
                    ModelShaderLayout.TextureFlagsOffset);
                if (hasTexture)
                    _commandList.SetGraphicsRootDescriptorTable(
                        ModelShaderLayout.ModelTextureRootParameter,
                        SrvGpuHandle(texture!.SrvSlot));
                else
                    _commandList.SetGraphicsRootDescriptorTable(
                        ModelShaderLayout.ModelTextureRootParameter,
                        SrvGpuHandle(DebugOverlaySrvSlot));

                _commandList.DrawIndexedInstanced((uint)drawCount, 1, (uint)surface.IndexStart, 0, 0);
            }
        }

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_pipelineState);
    }

    private unsafe void SetModelRootConstantsIfChanged(
        int rootParameterIndex,
        float* constants,
        int count,
        int destinationOffset)
    {
        if (_modelConstantCache.Matches(rootParameterIndex, constants, count, destinationOffset))
            return;

        _commandList.SetGraphicsRoot32BitConstants(
            (uint)rootParameterIndex,
            (uint)count,
            (nint)constants,
            (uint)destinationOffset);
        _modelConstantCache.Store(rootParameterIndex, constants, count, destinationOffset);
    }

    private void PruneUnusedModelTextures(IReadOnlyList<SceneModel> models)
    {
        var activeTextureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            foreach (var surface in model.Mesh.Surfaces)
            {
                if (!string.IsNullOrWhiteSpace(surface.TextureName))
                    activeTextureNames.Add(surface.TextureName);
            }
        }

        var textureNamesToRemove = new List<string>();
        foreach (var pair in _modelTextures)
        {
            if (activeTextureNames.Contains(pair.Key))
                continue;

            textureNamesToRemove.Add(pair.Key);
            var texture = pair.Value;
            texture.Resource?.Dispose();
            if (texture.SrvSlot >= 0)
                _freeModelSrvSlots.Push(texture.SrvSlot);
        }

        foreach (var textureName in textureNamesToRemove)
            _modelTextures.Remove(textureName);
    }

    private ModelGpuMesh GetOrCreateModelGpuMesh(Mesh mesh)
    {
        if (_modelMeshes.TryGetValue(mesh, out var gpuMesh))
            return gpuMesh;

        var vertexBytes = MemoryMarshal.AsBytes(mesh.Vertices.AsSpan());
        var indexBytes = MemoryMarshal.AsBytes(mesh.Indices.AsSpan());
        var vertexBuffer = _textureUploader.CreateUploadBuffer(vertexBytes);
        var indexBuffer = _textureUploader.CreateUploadBuffer(indexBytes);

        gpuMesh = new ModelGpuMesh(
            vertexBuffer,
            indexBuffer,
            new VertexBufferView(vertexBuffer.GPUVirtualAddress, (uint)vertexBytes.Length, (uint)ModelVertexStride),
            new IndexBufferView(indexBuffer.GPUVirtualAddress, (uint)indexBytes.Length, Format.R16_UInt),
            mesh.Indices.Length);

        _modelMeshes.Add(mesh, gpuMesh);
        return gpuMesh;
    }

    private static unsafe void WriteMatrix(Matrix4x4 matrix, float* target)
    {
        target[0] = matrix.M11;
        target[1] = matrix.M12;
        target[2] = matrix.M13;
        target[3] = matrix.M14;
        target[4] = matrix.M21;
        target[5] = matrix.M22;
        target[6] = matrix.M23;
        target[7] = matrix.M24;
        target[8] = matrix.M31;
        target[9] = matrix.M32;
        target[10] = matrix.M33;
        target[11] = matrix.M34;
        target[12] = matrix.M41;
        target[13] = matrix.M42;
        target[14] = matrix.M43;
        target[15] = matrix.M44;
    }

    private static unsafe void WriteModelColor(string name, float* target)
    {
        var hash = (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(name);
        target[0] = 0.35f + ((hash & 0xFF) / 255.0f) * 0.55f;
        target[1] = 0.35f + (((hash >> 8) & 0xFF) / 255.0f) * 0.55f;
        target[2] = 0.35f + (((hash >> 16) & 0xFF) / 255.0f) * 0.55f;
        target[3] = 1.0f;
    }

    private static unsafe void WriteModelTextureFlags(
        bool hasTexture,
        float localDepthScale,
        float painterDepth,
        float alphaCutoff,
        float* target)
    {
        target[0] = hasTexture ? 1.0f : 0.0f;
        target[1] = localDepthScale;
        target[2] = painterDepth;
        target[3] = alphaCutoff;
    }

    private static unsafe void WriteModelLighting(
        SacredCamera camera,
        SceneLighting lighting,
        Dx12DisplayProfile display,
        float* target)
    {
        target[0] = lighting.LightPosition.X;
        target[1] = lighting.LightPosition.Y;
        target[2] = lighting.LightPosition.Z;
        target[3] = Math.Max(0.0f, lighting.SpecularIntensity);

        target[4] = camera.EyePosition.X;
        target[5] = camera.EyePosition.Y;
        target[6] = camera.EyePosition.Z;
        target[7] = Math.Max(1.0f, lighting.Shininess);

        target[8] = lighting.AmbientColor.X;
        target[9] = lighting.AmbientColor.Y;
        target[10] = lighting.AmbientColor.Z;
        target[11] = Math.Max(0.0f, lighting.AmbientIntensity);

        target[12] = lighting.LightColor.X;
        target[13] = lighting.LightColor.Y;
        target[14] = lighting.LightColor.Z;
        target[15] = Math.Max(0.0f, lighting.DiffuseIntensity);

        target[16] = Math.Max(0.0f, display.ScenePaperWhiteNits);
        target[17] = Math.Max(0.0f, display.UiPaperWhiteNits);
        target[18] = Math.Max(0.0f, display.SunDiffuseNits);
        target[19] = Math.Max(0.0f, display.SunSpecularNits);
    }

    private void PruneGpuSectorTextures(IReadOnlyList<TerrainSectorImage> sectorImages)
    {
        _sectorsToRemove.Clear();

        foreach (var coord in _sectorTextures.Keys)
        {
            var stillVisible = false;
            foreach (var image in sectorImages)
            {
                if (image.Coord != coord)
                    continue;

                stillVisible = true;
                break;
            }

            if (!stillVisible)
                _sectorsToRemove.Add(coord);
        }

        foreach (var coord in _sectorsToRemove)
        {
            var texture = _sectorTextures[coord];
            texture.Resource.Dispose();
            _freeSrvSlots.Push(texture.SrvSlot);
            _sectorTextures.Remove(coord);
        }
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
        _commandQueue.ExecuteCommandLists([_commandList]);
    }

    private void SectorUploadWorkerLoop()
    {
        foreach (var request in _sectorUploadRequests.GetConsumingEnumerable())
            _completedSectorUploads.Enqueue(UploadSectorTextureOnWorker(request));
    }

    private void SignalFrameFence()
    {
        _fenceValue++;
        _commandQueue.Signal(_fence, _fenceValue).CheckError();
        _commandsInFlight = true;
    }

    private void WaitForGpu()
    {
        if (!_commandsInFlight || _fence.CompletedValue >= _fenceValue)
        {
            _commandsInFlight = false;
            return;
        }

        _fence.SetEventOnCompletion(_fenceValue, _fenceEvent).CheckError();
        Kernel32.WaitForSingleObject(_fenceEvent, uint.MaxValue);
        _commandsInFlight = false;
    }

    private async Task WaitForGpuAsync(CancellationToken cancellationToken)
    {
        if (!_commandsInFlight || _fence.CompletedValue >= _fenceValue)
        {
            _commandsInFlight = false;
            return;
        }

        _fence.SetEventOnCompletion(_fenceValue, _fenceEvent).CheckError();
        await Win32EventAwaiter.WaitAsync(_fenceEvent, cancellationToken);
        _commandsInFlight = false;
    }

    private void DisposeUploadResources()
    {
        foreach (var resource in _uploadResources)
            resource.Dispose();
        _uploadResources.Clear();
    }

    private void StopSectorUploadWorker()
    {
        _sectorUploadRequests.CompleteAdding();
        _sectorUploadThread?.Join();
        _sectorUploadThread = null;

        while (_completedSectorUploads.TryDequeue(out var upload))
        {
            upload.Resource?.Dispose();
            _pendingSectorUploads.Remove(upload.Coord);
            _freeSrvSlots.Push(upload.SrvSlot);
        }

        _sectorUploadRequests.Dispose();
        _pendingSectorUploads.Clear();
    }

    private CpuDescriptorHandle RtvHandle(int index) =>
        _rtvHeap.GetCPUDescriptorHandleForHeapStart() + index * _rtvDescriptorSize;

    private CpuDescriptorHandle DsvHandle() =>
        _dsvHeap.GetCPUDescriptorHandleForHeapStart();

    private CpuDescriptorHandle SrvCpuHandle(int index) =>
        _srvHeap.GetCPUDescriptorHandleForHeapStart() + index * _srvDescriptorSize;

    private GpuDescriptorHandle SrvGpuHandle(int index) =>
        _srvHeap.GetGPUDescriptorHandleForHeapStart() + index * _srvDescriptorSize;

    private static class ModelShaderLayout
    {
        public const int RootParameterCount = 3;

        public const int ModelConstantsRegister = 0; // HLSL: register(b0)
        public const int SceneConstantsRegister = 1; // HLSL: register(b1)
        public const int ModelTextureRegister = 0; // HLSL: register(t0)
        public const int ModelSamplerRegister = 0; // HLSL: register(s0)

        public const int ModelConstantsRootParameter = 0;
        public const int ModelTextureRootParameter = 1;
        public const int SceneConstantsRootParameter = 2;

        public const int ModelConstantsCount = 40;
        public const int ModelBaseConstantsOffset = 0;
        public const int ModelBaseConstantsCount = 36;
        public const int TextureFlagsOffset = 36;
        public const int TextureFlagsConstantsCount = 4;

        public const int SceneConstantsCount = 20;
    }

    private sealed class RootConstantUploadCache
    {
        private readonly Slot[] _slots;

        public RootConstantUploadCache(int rootParameterCount)
        {
            _slots = new Slot[rootParameterCount];
            for (var i = 0; i < _slots.Length; i++)
                _slots[i] = new Slot();
        }

        public void Reset()
        {
            foreach (var slot in _slots)
                slot.Reset();
        }

        public unsafe bool Matches(int rootParameterIndex, float* values, int count, int destinationOffset) =>
            _slots[rootParameterIndex].Matches(values, count, destinationOffset);

        public unsafe void Store(int rootParameterIndex, float* values, int count, int destinationOffset) =>
            _slots[rootParameterIndex].Store(values, count, destinationOffset);

        private sealed class Slot
        {
            private int[] _bits = [];
            private bool[] _known = [];

            public void Reset()
            {
                Array.Clear(_known);
            }

            public unsafe bool Matches(float* values, int count, int destinationOffset)
            {
                if (destinationOffset < 0 || count < 0 || destinationOffset + count > _bits.Length)
                    return false;

                for (var i = 0; i < count; i++)
                {
                    var index = destinationOffset + i;
                    if (!_known[index] || _bits[index] != BitConverter.SingleToInt32Bits(values[i]))
                        return false;
                }

                return true;
            }

            public unsafe void Store(float* values, int count, int destinationOffset)
            {
                EnsureCapacity(destinationOffset + count);
                for (var i = 0; i < count; i++)
                {
                    var index = destinationOffset + i;
                    _bits[index] = BitConverter.SingleToInt32Bits(values[i]);
                    _known[index] = true;
                }
            }

            private void EnsureCapacity(int length)
            {
                if (_bits.Length >= length)
                    return;

                Array.Resize(ref _bits, length);
                Array.Resize(ref _known, length);
            }
        }
    }

    private sealed record SectorUploadRequest(TerrainSectorImage Image, int SrvSlot);

    private sealed record CompletedSectorUpload(SectorCoord Coord, ID3D12Resource? Resource, int SrvSlot, Exception? Error, int ReleasedCpuBytes);

    private sealed record SectorTexture(ID3D12Resource Resource, int SrvSlot);

    private sealed record StaticSpriteTexture(ID3D12Resource Resource, int SrvSlot);

    private sealed record CompletedModelTextureLoad(string TextureName, TextureAsset? Asset, Exception? Error);

    private sealed class ModelTexture(string name)
    {
        public string Name { get; } = name;
        public ID3D12Resource? Resource { get; set; }
        public int SrvSlot { get; set; } = -1;
        public bool Pending { get; set; }
        public bool Failed { get; set; }
    }

    private sealed record ModelGpuMesh(
        ID3D12Resource VertexBuffer,
        ID3D12Resource IndexBuffer,
        VertexBufferView VertexBufferView,
        IndexBufferView IndexBufferView,
        int IndexCount) : IDisposable
    {
        public void Dispose()
        {
            VertexBuffer.Dispose();
            IndexBuffer.Dispose();
        }
    }

}
