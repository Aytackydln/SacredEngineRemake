using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Granny;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;
using Sacred.Particles;
using Sacred.Shaders;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

internal sealed class Dx12ItemModelRenderer : IDisposable
{
    private const int FrameCount = 2;
    private const int MaxTextures = 128;
    private const int FallbackTextureSlot = 0;
    private const int FirstModelTextureSlot = 1;
    private const int GridColumns = 4;
    private const int GridRows = 5;
    private const float GridCellWorldSize = 36.0f;
    private const float GridFitPadding = 0.86f;
    private const float GridLineWorldThickness = 1.35f;
    private const float MinimumZoom = 0.45f;
    private const float MaximumZoom = 3.5f;
    private static readonly Vector4 UsableCellColor = new(0.40f, 0.40f, 0.40f, 0.20f);
    private static readonly Vector4 GridLineColor = new(0.48f, 0.48f, 0.0f, 0.52f);
    private static readonly int VertexStride = Marshal.SizeOf<VertexPositionNormalTexture>();
    private const Format BackBufferFormat = Format.R8G8B8A8_UNorm;
    private const Format DepthBufferFormat = Format.D32_Float;
    private static readonly TimeSpan ResizeDebounce = TimeSpan.FromMilliseconds(150);

    private readonly nint _hwnd;
    private readonly ID3D12Resource[] _backBuffers = new ID3D12Resource[FrameCount];
    private readonly Dictionary<string, ModelTexture> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private readonly ModelShaderConstantsUpdater _modelShaderConstants = new();
    private readonly Action _shaderReloadHandler;

    private IDXGIFactory2 _factory = null!;
    private ID3D12Device _device = null!;
    private ID3D12CommandQueue _commandQueue = null!;
    private IDXGISwapChain3 _swapChain = null!;
    private ID3D12DescriptorHeap _rtvHeap = null!;
    private ID3D12DescriptorHeap _dsvHeap = null!;
    private ID3D12DescriptorHeap _srvHeap = null!;
    private ID3D12CommandAllocator _commandAllocator = null!;
    private ID3D12GraphicsCommandList _commandList = null!;
    private ID3D12Fence _fence = null!;
    private ID3D12RootSignature _rootSignature = null!;
    private ID3D12PipelineState _pipelineState = null!;
    private ID3D12PipelineState _transparentModelPipelineState = null!;
    private ID3D12PipelineState _animatedPipelineState = null!;
    private ID3D12PipelineState _effectPipelineState = null!;
    private ID3D12PipelineState _transparentEffectPipelineState = null!;
    private ID3D12PipelineState _itemParticlePipelineState = null!;
    private ID3D12PipelineState _itemGlowPipelineState = null!;
    private ID3D12PipelineState _inventoryUiPipelineState = null!;
    private ID3D12Resource? _depthBuffer;
    private ID3D12Resource? _fallbackTexture;
    private Dx12TextureUploader _textureUploader = null!;
    private ModelGpuMesh? _inventoryUiMesh;
    private ModelGpuMesh? _mesh;
    private Mesh? _sourceMesh;
    private ModelGpuMesh? _selectedBoneHighlightMesh;
    private ModelGpuMesh? _equipmentEffectMesh;
    private InventoryUiSurface[] _inventoryUiSurfaces = [];
    private MeshBounds _meshBounds = new(Vector3.Zero, Vector3.Zero, Vector3.Zero, 1.0f);
    private MeshSurface[] _surfaces = [];
    private EquipmentEffectSurface[] _equipmentEffectSurfaces = [];
    private string _modelName = string.Empty;
    private Vector3 _previewRotation;
    private ItemPreviewRotationMode _rotationMode;
    private ItemPreviewPivotMode _pivotMode;
    private Vector3? _bonePivot;
    private Vector3? _selectedBonePosition;
    private GrnBoundsDiagnostics? _wholeModelBounds;
    private GrnBoundsDiagnostics? _skeletonBounds;
    private Vector2 _itemGridCenter;
    private float _modelScale = 1.0f;
    private int _itemGridWidth = 1;
    private int _itemGridHeight = 1;
    private float _userYaw;
    private float _userPitch;
    private float _userRoll;
    private float _zoom = 1.12f;
    private nint _fenceEvent;
    private int _rtvDescriptorSize;
    private int _srvDescriptorSize;
    private int _renderWidth;
    private int _renderHeight;
    private int _pendingRenderWidth;
    private int _pendingRenderHeight;
    private ulong _fenceValue;
    private bool _commandsInFlight;
    private bool _disposed;
    private int _shaderReloadPending;
    private long _lastResizeRequestTimestamp;

    public Dx12ItemModelRenderer(nint hwnd)
    {
        _hwnd = hwnd;
        _shaderReloadHandler = RequestShaderReload;
        CreateDevice();
        CreateSwapChain();
        CreateDescriptorHeaps();
        CreateBackBuffers();
        CreateDepthBuffer();
        CreateCommands();
        _textureUploader = new Dx12TextureUploader(_device);
        CreatePipeline();
        Dx12ShaderCatalog.Reloaded += _shaderReloadHandler;
        RebuildInventoryUiMesh(includeOccupiedCells: false);
        CreateFallbackTexture();
    }

    public void ClearModel()
    {
        WaitForGpu();
        _mesh?.Dispose();
        _mesh = null;
        _sourceMesh = null;
        _selectedBoneHighlightMesh?.Dispose();
        _selectedBoneHighlightMesh = null;
        _equipmentEffectMesh?.Dispose();
        _equipmentEffectMesh = null;
        _selectedBonePosition = null;
        _surfaces = [];
        _equipmentEffectSurfaces = [];
        _modelName = string.Empty;
        _itemGridWidth = 1;
        _itemGridHeight = 1;
        _itemGridCenter = Vector2.Zero;
        _modelScale = 1.0f;
        RebuildInventoryUiMesh(includeOccupiedCells: false);
        DisposeModelTextures();
    }

    public void SetModel(
        GrnAsset asset,
        Vector3 previewRotation,
        int gridWidth,
        int gridHeight,
        ItemPreviewRotationMode rotationMode,
        ItemPreviewPivotMode pivotMode,
        string? pivotBoneName,
        EquipmentEffectScene effectScene)
    {
        WaitForGpu();
        _mesh?.Dispose();
        _mesh = null;
        _sourceMesh = null;
        _selectedBoneHighlightMesh?.Dispose();
        _selectedBoneHighlightMesh = null;
        _equipmentEffectMesh?.Dispose();
        _equipmentEffectMesh = null;
        _surfaces = [];
        _equipmentEffectSurfaces = [];
        _modelName = asset.Name;
        _previewRotation = IsFinite(previewRotation) ? previewRotation : Vector3.Zero;
        _rotationMode = rotationMode;
        _pivotMode = pivotMode;
        _bonePivot = ResolveBonePivot(asset.Diagnostics, pivotMode, pivotBoneName);
        _selectedBonePosition = ResolveBonePosition(asset.Diagnostics, pivotBoneName);
        _wholeModelBounds = asset.Diagnostics?.WholeModelBounds;
        _skeletonBounds = asset.Diagnostics?.SkeletonBounds;
        _itemGridWidth = Math.Clamp(gridWidth, 1, GridColumns);
        _itemGridHeight = Math.Clamp(gridHeight, 1, GridRows);
        _itemGridCenter = CalculateOccupiedCellCenter(_itemGridWidth, _itemGridHeight);
        _modelScale = 1.0f;
        RebuildInventoryUiMesh(includeOccupiedCells: true);
        DisposeModelTextures();

        if (asset.Mesh is null || asset.Mesh.Vertices.Length == 0 || asset.Mesh.Indices.Length == 0)
            return;

        _meshBounds = CalculateBounds(asset.Mesh.Vertices);
        _modelScale = CalculateGridFitScale(asset.Mesh.Vertices, GetPivotPoint(), CreateItemRotationMatrix(), _itemGridWidth, _itemGridHeight);
        _mesh = UploadMesh(asset.Mesh);
        _sourceMesh = asset.Mesh;
        _selectedBoneHighlightMesh = CreateSelectedBoneHighlightMesh(_selectedBonePosition);
        if (effectScene.Mesh is { Vertices.Length: > 0, Indices.Length: > 0 } effectMesh)
        {
            _equipmentEffectMesh = UploadMesh(effectMesh);
            _equipmentEffectSurfaces = effectScene.Surfaces.ToArray();
        }
        _surfaces = asset.Mesh.Surfaces.Count == 0
            ? [new MeshSurface(0, asset.Mesh.Indices.Length, null)]
            : asset.Mesh.Surfaces.ToArray();
    }

    public void SetUserRotation(float yaw, float pitch, float roll)
    {
        _userYaw = yaw;
        _userPitch = pitch;
        _userRoll = roll;
    }

    public async Task SetTexturesAsync(
        IReadOnlyDictionary<string, ModelTextureBinding> textures,
        CancellationToken cancellationToken = default)
    {
        var uploaded = new List<UploadedModelTexture>();
        var slot = FirstModelTextureSlot;
        try
        {
            foreach (var pair in textures)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (slot >= MaxTextures)
                    break;

                var baseTexture = pair.Value.BaseTexture;
                var resource = await _textureUploader.UploadAsync(
                    baseTexture.Width,
                    baseTexture.Height,
                    baseTexture.Rgba8,
                    cancellationToken);
                ID3D12Resource? overlayResource = null;
                try
                {
                    var srvSlot = slot++;
                    var overlaySrvSlot = FallbackTextureSlot;
                    var overlayAnimation = TextureAnimation.None;
                    var overlayMode = TextureOverlayMode.None;
                    if (pair.Value.OverlayTexture is { } overlayTexture && slot < MaxTextures)
                    {
                        overlayResource = await _textureUploader.UploadAsync(
                            overlayTexture.Width,
                            overlayTexture.Height,
                            overlayTexture.Rgba8,
                            cancellationToken);
                        overlaySrvSlot = slot++;
                        overlayAnimation = overlayTexture.Animation;
                        overlayMode = pair.Value.OverlayMode;
                    }

                    uploaded.Add(new UploadedModelTexture(
                        pair.Key,
                        resource,
                        srvSlot,
                        baseTexture.Animation,
                        overlayResource,
                        overlaySrvSlot,
                        overlayAnimation,
                        overlayMode,
                        HasTranslucentPixels(baseTexture.Rgba8)));
                }
                catch
                {
                    overlayResource?.Dispose();
                    resource.Dispose();
                    throw;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            WaitForGpu();
            DisposeModelTextures();
            foreach (var texture in uploaded)
            {
                _device.CreateShaderResourceView(texture.Resource, null, SrvCpuHandle(texture.SrvSlot));
                if (texture.OverlayResource is not null)
                    _device.CreateShaderResourceView(texture.OverlayResource, null, SrvCpuHandle(texture.OverlaySrvSlot));
                _textures[texture.Name] = new ModelTexture(
                    texture.Resource,
                    texture.SrvSlot,
                    texture.Animation,
                    texture.OverlayResource,
                    texture.OverlaySrvSlot,
                    texture.OverlayAnimation,
                    texture.OverlayMode,
                    texture.HasTranslucentPixels);
            }
            uploaded.Clear();
        }
        finally
        {
            foreach (var texture in uploaded)
            {
                texture.Resource.Dispose();
                texture.OverlayResource?.Dispose();
            }
        }
    }

    public void ZoomBy(double delta)
    {
        if (delta == 0)
            return;

        _zoom = Math.Clamp(_zoom * (float)Math.Pow(1.12, delta), MinimumZoom, MaximumZoom);
    }

    public void RenderFrame()
    {
        if (_disposed)
            return;

        WaitForGpu();
        ReloadShadersIfRequested();
        ResizeIfNeeded();

        _commandAllocator.Reset();
        _commandList.Reset(_commandAllocator, _pipelineState);
        RecordFrame();
        _commandList.Close();

        _commandQueue.ExecuteCommandLists([_commandList]);
        _swapChain.Present(1, PresentFlags.None);
        SignalFrameFence();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Dx12ShaderCatalog.Reloaded -= _shaderReloadHandler;
        WaitForGpu();
        ClearModel();

        _fallbackTexture?.Dispose();
        _fallbackTexture = null;
        _textureUploader.Dispose();
        _inventoryUiMesh?.Dispose();
        _inventoryUiMesh = null;
        _selectedBoneHighlightMesh?.Dispose();
        _selectedBoneHighlightMesh = null;
        _depthBuffer?.Dispose();
        _depthBuffer = null;

        foreach (var backBuffer in _backBuffers)
            backBuffer?.Dispose();

        DisposePipelineResources();
        _fence.Dispose();
        _commandList.Dispose();
        _commandAllocator.Dispose();
        _srvHeap.Dispose();
        _dsvHeap.Dispose();
        _rtvHeap.Dispose();
        _swapChain.Dispose();
        _commandQueue.Dispose();
        _device.Dispose();
        _factory.Dispose();

        if (_fenceEvent != 0)
        {
            Win32Native.CloseHandle(_fenceEvent);
            _fenceEvent = 0;
        }
    }

    private void CreateDevice()
    {
        _factory = CreateDXGIFactory2<IDXGIFactory2>(false);
        _factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAltEnter).CheckError();
        _device = D3D12CreateDevice<ID3D12Device>(null, FeatureLevel.Level_11_0);
        _commandQueue = _device.CreateCommandQueue(CommandListType.Direct);
    }

    private void CreateSwapChain()
    {
        GetClientSize(out _renderWidth, out _renderHeight);
        var description = new SwapChainDescription1(
            (uint)_renderWidth,
            (uint)_renderHeight,
            BackBufferFormat,
            false,
            Usage.RenderTargetOutput,
            FrameCount,
            Scaling.Stretch,
            SwapEffect.FlipDiscard,
            AlphaMode.Ignore,
            SwapChainFlags.None);

        using var swapChain1 = _factory.CreateSwapChainForHwnd(_commandQueue, _hwnd, description, null, null);
        _swapChain = swapChain1.QueryInterface<IDXGISwapChain3>();
    }

    private void CreateDescriptorHeaps()
    {
        _rtvHeap = CreateDescriptorHeap(DescriptorHeapType.RenderTargetView, FrameCount, DescriptorHeapFlags.None);
        _dsvHeap = CreateDescriptorHeap(DescriptorHeapType.DepthStencilView, 1, DescriptorHeapFlags.None);
        _srvHeap = CreateDescriptorHeap(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, MaxTextures, DescriptorHeapFlags.ShaderVisible);
        _rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        _srvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
    }

    private ID3D12DescriptorHeap CreateDescriptorHeap(DescriptorHeapType type, int count, DescriptorHeapFlags flags)
    {
        var description = new DescriptorHeapDescription(type, (uint)count, flags, 0);
        return _device.CreateDescriptorHeap(in description);
    }

    private void CreateBackBuffers()
    {
        for (var i = 0; i < FrameCount; i++)
        {
            _backBuffers[i] = _swapChain.GetBuffer<ID3D12Resource>((uint)i);
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
        _depthBuffer = _device.CreateCommittedResource(heapProperties, HeapFlags.None, description, ResourceStates.DepthWrite, clearValue);
        _device.CreateDepthStencilView(_depthBuffer, null, DsvHandle());
    }

    private void CreateCommands()
    {
        _commandAllocator = _device.CreateCommandAllocator(CommandListType.Direct);
        _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, _commandAllocator, null);
        _commandList.Close();
        _fence = _device.CreateFence(0, FenceFlags.None);
        _fenceEvent = Win32Native.CreateEvent(0, false, false, null);
        if (_fenceEvent == 0)
            throw new InvalidOperationException("Failed to create D3D12 fence event.");
    }

    private void CreatePipeline()
    {
        var shaders = Dx12ShaderCatalog.Sdr;
        var definition = Dx12PipelineCatalog.CreateModels(shaders, new Dx12ModelPipelineOptions
        {
            IncludeDenseParticle = false,
            IncludeInventoryUi = true,
            SamplerAddressMode = TextureAddressMode.Wrap,
            SamplerBorderColor = StaticBorderColor.OpaqueWhite
        });
        var compiled = Dx12PipelineFactory.Compile(definition, D3DShaderCompiler.Compile);
        var pipelines = Dx12PipelineFactory.Create(_device, compiled, BackBufferFormat, DepthBufferFormat);

        _rootSignature = pipelines.RootSignature;
        _pipelineState = pipelines[Dx12PipelineKind.StaticModel];
        _transparentModelPipelineState = pipelines[Dx12PipelineKind.TransparentModel];
        _animatedPipelineState = pipelines[Dx12PipelineKind.AnimatedModel];
        _effectPipelineState = pipelines[Dx12PipelineKind.EffectModel];
        _transparentEffectPipelineState = pipelines[Dx12PipelineKind.TransparentEffectModel];
        _itemParticlePipelineState = pipelines[Dx12PipelineKind.TransparentItemParticle];
        _itemGlowPipelineState = pipelines[Dx12PipelineKind.ItemGlow];
        _inventoryUiPipelineState = pipelines[Dx12PipelineKind.InventoryUi];
    }

    private void RequestShaderReload() => Interlocked.Exchange(ref _shaderReloadPending, 1);

    private void ReloadShadersIfRequested()
    {
        if (Interlocked.Exchange(ref _shaderReloadPending, 0) == 0)
            return;

        DisposePipelineResources();
        CreatePipeline();
    }

    private void DisposePipelineResources()
    {
        _inventoryUiPipelineState?.Dispose();
        _itemGlowPipelineState?.Dispose();
        _itemParticlePipelineState?.Dispose();
        _effectPipelineState?.Dispose();
        _transparentEffectPipelineState?.Dispose();
        _animatedPipelineState?.Dispose();
        _pipelineState?.Dispose();
        _transparentModelPipelineState?.Dispose();
        _rootSignature?.Dispose();
    }

    private void CreateFallbackTexture()
    {
        var rgba = new byte[]
        {
            144, 174, 120, 255,
            95, 125, 88, 255,
            95, 125, 88, 255,
            144, 174, 120, 255
        };
        _fallbackTexture = _textureUploader.UploadAsync(2, 2, rgba).GetAwaiter().GetResult();
        _device.CreateShaderResourceView(_fallbackTexture, null, SrvCpuHandle(FallbackTextureSlot));
    }

    private void ResizeIfNeeded()
    {
        GetClientSize(out var width, out var height);
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

        foreach (var backBuffer in _backBuffers)
            backBuffer.Dispose();

        _swapChain.ResizeBuffers(FrameCount, (uint)_pendingRenderWidth, (uint)_pendingRenderHeight, BackBufferFormat, SwapChainFlags.None).CheckError();
        _renderWidth = _pendingRenderWidth;
        _renderHeight = _pendingRenderHeight;
        _pendingRenderWidth = 0;
        _pendingRenderHeight = 0;
        CreateBackBuffers();
        CreateDepthBuffer();
    }

    private void RecordFrame()
    {
        var frameIndex = _swapChain.CurrentBackBufferIndex;
        var backBuffer = _backBuffers[frameIndex];
        var rtv = RtvHandle((int)frameIndex);
        var dsv = DsvHandle();

        Transition(backBuffer, ResourceStates.Present, ResourceStates.RenderTarget);

        var viewport = new Viewport(0, 0, _renderWidth, _renderHeight, 0.0f, 1.0f);
        var scissor = new RawRect(0, 0, _renderWidth, _renderHeight);
        _commandList.RSSetViewports(viewport);
        _commandList.RSSetScissorRects(scissor);
        _commandList.OMSetRenderTargets(rtv, dsv);
        _commandList.ClearRenderTargetView(rtv, new Color4(0.05f, 0.10f, 0.09f, 1.0f));
        _commandList.ClearDepthStencilView(dsv, ClearFlags.Depth, 1.0f, 0, 0, []);

        RecordInventoryUi();
        if (_mesh is not null)
        {
            RecordModel();
            RecordEquipmentEffects();
            RecordSelectedBoneHighlight();
        }

        Transition(backBuffer, ResourceStates.RenderTarget, ResourceStates.Present);
    }

    private unsafe void RecordModel()
    {
        var mesh = _mesh!;
        var vertexBufferView = mesh.VertexBufferView;
        var indexBufferView = mesh.IndexBufferView;

        _commandList.SetDescriptorHeaps([_srvHeap]);
        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_pipelineState);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commandList.IASetVertexBuffers(0, 1, &vertexBufferView);
        _commandList.IASetIndexBuffer(&indexBufferView);

        var sceneConstants = stackalloc float[ModelShaderLayout.SceneConstantsCount];
        WritePreviewSceneConstants(sceneConstants);
        _commandList.SetGraphicsRoot32BitConstants(
            ModelShaderLayout.SceneConstantsRootParameter,
            ModelShaderLayout.SceneConstantsCount,
            sceneConstants,
            0);

        var constants = stackalloc float[ModelShaderLayout.ModelConstantsCount];
        var world = CreateWorldMatrix();
        var wvp = world * CreateViewProjectionMatrix();
        _modelShaderConstants.WriteModelBase(
            constants,
            wvp,
            world,
            ModelShaderVariables.ColorFromName(_modelName));
        var defaultModelColor = ModelShaderVariables.ColorFromName(_modelName);
        _modelShaderConstants.WriteTextureFlags(
            constants + ModelShaderLayout.TextureFlagsOffset,
            ModelShaderVariables.TextureModeNoTexture,
            ModelShaderVariables.TextureAnimationNone,
            ModelShaderLayout.PreserveProjectedDepth,
            scaledAnimationTime: 0.0f);
        var elapsedSeconds = (float)Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds;

        for (var passIndex = 0; passIndex < 3; passIndex++)
        {
            var pass = (ModelSurfacePass)passIndex;
            foreach (var surface in _surfaces)
            {
                if (surface.IndexCount <= 0 || surface.IndexStart >= mesh.IndexCount)
                    continue;

                var texture = ResolveTexture(surface.TextureName);
                var animatesBase = texture?.Animation.IsAnimated == true;
                var hasOverlay = texture?.OverlayResource is not null &&
                                 texture.OverlayMode != TextureOverlayMode.None;
                var animatesOverlay = hasOverlay && texture!.OverlayAnimation.IsAnimated;
                var drawCount = Math.Min(surface.IndexCount, mesh.IndexCount - surface.IndexStart);
                var hasTexture = texture is not null;
                var animation = TextureAnimation.None;
                var drawOverlay = false;
                if (pass == ModelSurfacePass.AnimatedBase)
                {
                    if (!animatesBase || !hasTexture)
                        continue;

                    hasOverlay = false;
                    animation = texture!.Animation;
                }
                else if (pass == ModelSurfacePass.EffectOverlay)
                {
                    if (animatesBase || !animatesOverlay || !hasTexture)
                        continue;

                    drawOverlay = true;
                    animation = texture!.OverlayAnimation;
                }
                else
                {
                    if (animatesBase || (hasOverlay && animatesOverlay))
                        continue;

                    hasOverlay = hasOverlay && !animatesOverlay;
                }

                _commandList.SetPipelineState(pass switch
                {
                    ModelSurfacePass.AnimatedBase => _animatedPipelineState,
                    ModelSurfacePass.EffectOverlay => _effectPipelineState,
                    _ when texture?.HasTranslucentPixels == true => _transparentModelPipelineState,
                    _ => _pipelineState
                });

                var modelColor = animation.Mode == TextureAnimationMode.RadialSweepBlackKey &&
                                 _sourceMesh is not null &&
                                 MeshSurfaceRadialSweep.TryCalculate(_sourceMesh, surface, out var radialSweep)
                    ? radialSweep
                    : defaultModelColor;
                _modelShaderConstants.WriteModelColor(constants + 32, modelColor);

                _modelShaderConstants.WriteTextureFlags(
                    constants + ModelShaderLayout.TextureFlagsOffset,
                    ModelShaderVariables.PackTextureMode(
                        hasTexture,
                        drawOverlay || hasOverlay,
                        drawOverlay || texture?.OverlayMode == TextureOverlayMode.MultiTextureFill),
                    ModelShaderVariables.PackTextureAnimation(
                        animation.IsAnimated,
                        animation.Mode == TextureAnimationMode.RadialSweepBlackKey,
                        overlay: false),
                    ModelShaderLayout.PreserveProjectedDepth,
                    animation.IsAnimated ? elapsedSeconds * animation.TimeScale : 0.0f);
                _commandList.SetGraphicsRoot32BitConstants(
                    ModelShaderLayout.ModelConstantsRootParameter,
                    ModelShaderLayout.ModelConstantsCount,
                    constants,
                    0);
                _commandList.SetGraphicsRootDescriptorTable(
                    ModelShaderLayout.ModelTextureRootParameter,
                    SrvGpuHandle(texture?.SrvSlot ?? FallbackTextureSlot));
                _commandList.SetGraphicsRootDescriptorTable(
                    ModelShaderLayout.ModelOverlayTextureRootParameter,
                    SrvGpuHandle(drawOverlay || hasOverlay ? texture!.OverlaySrvSlot : FallbackTextureSlot));
                _commandList.DrawIndexedInstanced((uint)drawCount, 1, (uint)surface.IndexStart, 0, 0);
            }
        }
    }

    private unsafe void RecordEquipmentEffects()
    {
        var mesh = _equipmentEffectMesh;
        if (mesh is null || _equipmentEffectSurfaces.Length == 0)
            return;

        var vertexBufferView = mesh.VertexBufferView;
        var indexBufferView = mesh.IndexBufferView;
        _commandList.SetDescriptorHeaps([_srvHeap]);
        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_itemParticlePipelineState);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commandList.IASetVertexBuffers(0, 1, &vertexBufferView);
        _commandList.IASetIndexBuffer(&indexBufferView);

        var sceneConstants = stackalloc float[ModelShaderLayout.SceneConstantsCount];
        WritePreviewSceneConstants(sceneConstants);
        _commandList.SetGraphicsRoot32BitConstants(
            ModelShaderLayout.SceneConstantsRootParameter,
            ModelShaderLayout.SceneConstantsCount,
            sceneConstants,
            0);

        var constants = stackalloc float[ModelShaderLayout.ModelConstantsCount];
        var world = CreateWorldMatrix();
        var viewProjection = CreateViewProjectionMatrix();
        var elapsedSeconds = (float)Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds;

        foreach (var surface in _equipmentEffectSurfaces)
        {
            var shaderKind = ParticleShaderCatalog.ForMode(surface.TextureMode);
            _commandList.SetPipelineState(shaderKind == ParticleShaderKind.ItemGlow
                ? _itemGlowPipelineState
                : _itemParticlePipelineState);

            var texture = ResolveTexture(surface.TextureName);
            if (texture is null || surface.IndexCount <= 0 || surface.IndexStart >= mesh.IndexCount)
                continue;

            var effectWorld = world;
            if (surface.TextureMode == ParticleTextureMode.PoisonStatic &&
                surface.MotionVector.LengthSquared() > 0.0001f)
            {
                var progress = (elapsedSeconds * 0.42f + surface.Phase) % 1.0f;
                effectWorld = Matrix4x4.CreateTranslation(surface.MotionVector * progress) * world;
            }

            _modelShaderConstants.WriteModelBase(constants, viewProjection, effectWorld, surface.Color);
            _modelShaderConstants.WriteTextureFlags(
                constants + ModelShaderLayout.TextureFlagsOffset,
                (float)surface.TextureMode,
                ModelShaderLayout.PreserveProjectedDepth,
                surface.Phase,
                elapsedSeconds);
            _commandList.SetGraphicsRoot32BitConstants(
                ModelShaderLayout.ModelConstantsRootParameter,
                ModelShaderLayout.ModelConstantsCount,
                constants,
                0);
            _commandList.SetGraphicsRootDescriptorTable(
                ModelShaderLayout.ModelTextureRootParameter,
                SrvGpuHandle(texture.SrvSlot));
            _commandList.SetGraphicsRootDescriptorTable(
                ModelShaderLayout.ModelOverlayTextureRootParameter,
                SrvGpuHandle(FallbackTextureSlot));
            var drawCount = Math.Min(surface.IndexCount, mesh.IndexCount - surface.IndexStart);
            _commandList.DrawIndexedInstanced((uint)drawCount, 1, (uint)surface.IndexStart, 0, 0);
        }
    }

    private unsafe void RecordInventoryUi()
    {
        var mesh = _inventoryUiMesh;
        if (mesh is null || _inventoryUiSurfaces.Length == 0)
            return;

        var vertexBufferView = mesh.VertexBufferView;
        var indexBufferView = mesh.IndexBufferView;

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_inventoryUiPipelineState);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commandList.IASetVertexBuffers(0, 1, &vertexBufferView);
        _commandList.IASetIndexBuffer(&indexBufferView);

        var constants = stackalloc float[ModelShaderLayout.ModelConstantsCount];
        var world = CreateGridWorldMatrix();
        var wvp = world * CreateViewProjectionMatrix();

        foreach (var surface in _inventoryUiSurfaces)
        {
            _modelShaderConstants.WriteModelBase(constants, wvp, world, surface.Color);
            _modelShaderConstants.WriteTextureFlags(
                constants + ModelShaderLayout.TextureFlagsOffset,
                ModelShaderVariables.TextureModeNoTexture,
                ModelShaderVariables.TextureAnimationNone,
                painterDepth: 0.0f,
                scaledAnimationTime: 0.0f);
            _commandList.SetGraphicsRoot32BitConstants(
                ModelShaderLayout.ModelConstantsRootParameter,
                ModelShaderLayout.ModelConstantsCount,
                constants,
                0);
            _commandList.DrawIndexedInstanced((uint)surface.IndexCount, 1, (uint)surface.IndexStart, 0, 0);
        }
    }

    private unsafe void RecordSelectedBoneHighlight()
    {
        var mesh = _selectedBoneHighlightMesh;
        if (mesh is null)
            return;

        var vertexBufferView = mesh.VertexBufferView;
        var indexBufferView = mesh.IndexBufferView;
        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_inventoryUiPipelineState);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commandList.IASetVertexBuffers(0, 1, &vertexBufferView);
        _commandList.IASetIndexBuffer(&indexBufferView);

        var constants = stackalloc float[ModelShaderLayout.ModelConstantsCount];
        var world = CreateWorldMatrix();
        _modelShaderConstants.WriteModelBase(
            constants,
            world * CreateViewProjectionMatrix(),
            world,
            new Vector4(1.0f, 0.88f, 0.08f, 1.0f));
        _modelShaderConstants.WriteTextureFlags(
            constants + ModelShaderLayout.TextureFlagsOffset,
            ModelShaderVariables.TextureModeNoTexture,
            ModelShaderVariables.TextureAnimationNone,
            ModelShaderLayout.PreserveProjectedDepth,
            scaledAnimationTime: 0.0f);
        _commandList.SetGraphicsRoot32BitConstants(
            ModelShaderLayout.ModelConstantsRootParameter,
            ModelShaderLayout.ModelConstantsCount,
            constants,
            0);
        _commandList.DrawIndexedInstanced((uint)mesh.IndexCount, 1, 0, 0, 0);
    }

    private Matrix4x4 CreateWorldMatrix()
    {
        var userRotation = Matrix4x4.CreateFromYawPitchRoll(_userYaw, _userPitch, _userRoll);
        var rotation = CreateItemRotationMatrix() * userRotation;
        return Matrix4x4.CreateTranslation(-GetPivotPoint()) *
               rotation *
               Matrix4x4.CreateScale(_modelScale) *
               Matrix4x4.CreateTranslation(_itemGridCenter.X, 0.0f, _itemGridCenter.Y);
    }

    private Matrix4x4 CreateItemRotationMatrix()
    {
        var rotation = _previewRotation;
        return _rotationMode switch
        {
            ItemPreviewRotationMode.LegacyCurrent => CreateLegacyViewerPreviewRotation(rotation),
            ItemPreviewRotationMode.RawXyz => Matrix4x4.CreateRotationX(rotation.X) *
                                             Matrix4x4.CreateRotationY(rotation.Y) *
                                             Matrix4x4.CreateRotationZ(rotation.Z),
            ItemPreviewRotationMode.DirectYawPitchRoll => Matrix4x4.CreateFromYawPitchRoll(rotation.X, rotation.Y, rotation.Z),
            ItemPreviewRotationMode.GrnMatrix => CreateGrnRotationMatrix(rotation),
            _ => Matrix4x4.CreateFromYawPitchRoll(0, 0, 0),
        };
    }

    private static Matrix4x4 CreateLegacyViewerPreviewRotation(Vector3 r)
    {
        var rotation = CanonicalizePreviewRotation(r);

        // Weapon.pak armor rotations are authored in direct yaw/pitch/roll order, while weapon entries
        // use the legacy item-viewer order that is already handled by Dx12ItemModelRenderer.
        return Matrix4x4.CreateFromYawPitchRoll(rotation.Y, rotation.Z, rotation.X);
    }

    private static Vector3 CanonicalizePreviewRotation(Vector3 rotation)
    {
        var x = rotation.X;
        var y = rotation.Y;
        while (y > MathF.PI)
        {
            x -= MathF.PI;
            y -= MathF.PI;
        }

        while (y < -MathF.PI)
        {
            x += MathF.PI;
            y += MathF.PI;
        }

        return new Vector3(
            NormalizeAngle(x),
            NormalizeAngle(y),
            NormalizeAngle(rotation.Z));
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI)
            angle -= MathF.Tau;
        while (angle < -MathF.PI)
            angle += MathF.Tau;
        return angle;
    }

    private Vector3 GetPivotPoint()
    {
        return _pivotMode switch
        {
            ItemPreviewPivotMode.ModelOrigin => Vector3.Zero,
            ItemPreviewPivotMode.BoundsBottomCenter => new Vector3(_meshBounds.Center.X, _meshBounds.Center.Y, _meshBounds.Min.Z),
            ItemPreviewPivotMode.BoundsTopCenter => new Vector3(_meshBounds.Center.X, _meshBounds.Center.Y, _meshBounds.Max.Z),
            ItemPreviewPivotMode.BoundsCenterGround => new Vector3(_meshBounds.Center.X, _meshBounds.Center.Y, 0.0f),
            ItemPreviewPivotMode.WholeModelBoundsCenter when _wholeModelBounds is { } whole => whole.Center,
            ItemPreviewPivotMode.WholeModelBoundsBottomCenter when _wholeModelBounds is { } whole => new Vector3(whole.Center.X, whole.Center.Y, whole.Min.Z),
            ItemPreviewPivotMode.WholeModelBoundsTopCenter when _wholeModelBounds is { } whole => new Vector3(whole.Center.X, whole.Center.Y, whole.Max.Z),
            ItemPreviewPivotMode.WholeRigCenter when _skeletonBounds is { } rig => rig.Center,
            ItemPreviewPivotMode.WholeRigFeetCenter when _skeletonBounds is { } rig => new Vector3(rig.Center.X, rig.Center.Y, rig.Min.Z),
            ItemPreviewPivotMode.WholeRigTopCenter when _skeletonBounds is { } rig => new Vector3(rig.Center.X, rig.Center.Y, rig.Max.Z),
            ItemPreviewPivotMode.RootBone or ItemPreviewPivotMode.SelectedBone when _bonePivot is { } bonePivot => bonePivot,
            _ => _meshBounds.Center
        };
    }

    private static Vector3? ResolveBonePivot(
        GrnModelDiagnostics? diagnostics,
        ItemPreviewPivotMode pivotMode,
        string? pivotBoneName)
    {
        var bones = diagnostics?.Slices.SelectMany(static slice => slice.Bones).ToArray() ?? [];
        if (pivotMode == ItemPreviewPivotMode.RootBone)
            return bones.FirstOrDefault(static bone => bone.ParentIndex == bone.Index)?.Position;
        if (pivotMode == ItemPreviewPivotMode.SelectedBone && !string.IsNullOrWhiteSpace(pivotBoneName))
            return bones.FirstOrDefault(bone => bone.Name.Equals(pivotBoneName, StringComparison.OrdinalIgnoreCase))?.Position;
        return null;
    }

    private static Vector3? ResolveBonePosition(GrnModelDiagnostics? diagnostics, string? boneName)
    {
        if (string.IsNullOrWhiteSpace(boneName))
            return null;

        return diagnostics?.Slices
            .SelectMany(static slice => slice.Bones)
            .FirstOrDefault(bone => bone.Name.Equals(boneName, StringComparison.OrdinalIgnoreCase))
            ?.Position;
    }

    private static Matrix4x4 CreateGrnRotationMatrix(Vector3 rotation)
    {
        var cr = MathF.Cos(rotation.X);
        var sr = MathF.Sin(rotation.X);
        var cp = MathF.Cos(rotation.Y);
        var sp = MathF.Sin(rotation.Y);
        var cy = MathF.Cos(rotation.Z);
        var sy = MathF.Sin(rotation.Z);
        var srsp = sr * sp;
        var crsp = cr * sp;

        // Mirrors grn_format's setRotationRadians matrix, transposed for System.Numerics row-vector use.
        return new Matrix4x4(
            cp * cy, srsp * cy - cr * sy, crsp * cy + sr * sy, 0.0f,
            cp * sy, srsp * sy + cr * cy, crsp * sy - sr * cy, 0.0f,
            -sp, sr * cp, cr * cp, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);
    }

    private Matrix4x4 CreateGridWorldMatrix()
    {
        return Matrix4x4.Identity;
    }

    private Matrix4x4 CreateViewProjectionMatrix()
    {
        var sceneRadius = Math.Max(GridCellWorldSize * 3.0f, _meshBounds.Radius * _modelScale * 2.0f);
        var distance = Math.Max(120.0f, sceneRadius * 3.25f);
        var aspect = Math.Max(0.1f, _renderWidth / (float)Math.Max(1, _renderHeight));
        var eye = new Vector3(0.0f, -distance, sceneRadius * 0.18f);
        var target = Vector3.Zero;
        var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitZ);
        var gridWidth = GridColumns * GridCellWorldSize;
        var gridHeight = GridRows * GridCellWorldSize;
        var orthographicHeight = Math.Max(gridHeight, gridWidth / aspect) * 1.18f / _zoom;
        var projection = Matrix4x4.CreateOrthographic(
            orthographicHeight * aspect,
            orthographicHeight,
            0.5f,
            distance + sceneRadius * 6.0f);
        return view * projection;
    }

    private ModelTexture? ResolveTexture(string? textureName)
    {
        if (!string.IsNullOrWhiteSpace(textureName) && _textures.TryGetValue(textureName, out var texture))
            return texture;

        return null;
    }

    private void RebuildInventoryUiMesh(bool includeOccupiedCells)
    {
        _inventoryUiMesh?.Dispose();
        _inventoryUiMesh = null;

        var vertices = new List<VertexPositionNormalTexture>();
        var indices = new List<ushort>();
        var surfaces = new List<InventoryUiSurface>();

        if (includeOccupiedCells)
        {
            var occupied = CalculateOccupiedCellBounds(_itemGridWidth, _itemGridHeight);
            var start = indices.Count;
            AddQuad(vertices, indices, occupied.MinX, occupied.MinZ, occupied.MaxX, occupied.MaxZ);
            surfaces.Add(new InventoryUiSurface(start, indices.Count - start, UsableCellColor));
        }

        var lineStart = indices.Count;
        var halfWidth = GridColumns * GridCellWorldSize * 0.5f;
        var halfHeight = GridRows * GridCellWorldSize * 0.5f;
        for (var column = 0; column <= GridColumns; column++)
        {
            var x = -halfWidth + column * GridCellWorldSize;
            AddQuad(
                vertices,
                indices,
                x - GridLineWorldThickness * 0.5f,
                -halfHeight,
                x + GridLineWorldThickness * 0.5f,
                halfHeight);
        }

        for (var row = 0; row <= GridRows; row++)
        {
            var z = -halfHeight + row * GridCellWorldSize;
            AddQuad(
                vertices,
                indices,
                -halfWidth,
                z - GridLineWorldThickness * 0.5f,
                halfWidth,
                z + GridLineWorldThickness * 0.5f);
        }

        surfaces.Add(new InventoryUiSurface(lineStart, indices.Count - lineStart, GridLineColor));
        _inventoryUiMesh = UploadMesh(new Mesh(vertices.ToArray(), indices.ToArray()));
        _inventoryUiSurfaces = surfaces.ToArray();
    }

    private static void AddQuad(
        List<VertexPositionNormalTexture> vertices,
        List<ushort> indices,
        float minX,
        float minZ,
        float maxX,
        float maxZ)
    {
        if (vertices.Count > ushort.MaxValue - 4)
            throw new InvalidOperationException("Inventory UI mesh is too large for 16-bit indices.");

        var start = (ushort)vertices.Count;
        var normal = new Vector3(0.0f, -1.0f, 0.0f);
        vertices.Add(new VertexPositionNormalTexture(new Vector3(minX, 0.0f, maxZ), normal, new Vector2(0.0f, 0.0f)));
        vertices.Add(new VertexPositionNormalTexture(new Vector3(maxX, 0.0f, maxZ), normal, new Vector2(1.0f, 0.0f)));
        vertices.Add(new VertexPositionNormalTexture(new Vector3(maxX, 0.0f, minZ), normal, new Vector2(1.0f, 1.0f)));
        vertices.Add(new VertexPositionNormalTexture(new Vector3(minX, 0.0f, minZ), normal, new Vector2(0.0f, 1.0f)));
        indices.Add(start);
        indices.Add((ushort)(start + 1));
        indices.Add((ushort)(start + 2));
        indices.Add(start);
        indices.Add((ushort)(start + 2));
        indices.Add((ushort)(start + 3));
    }

    private ModelGpuMesh? CreateSelectedBoneHighlightMesh(Vector3? bonePosition)
    {
        if (bonePosition is not { } position || !IsFinite(position))
            return null;

        var radius = Math.Clamp(GridCellWorldSize * 0.09f / Math.Max(_modelScale, 0.001f), 0.1f, 25.0f);
        return UploadMesh(BoneHighlightMeshFactory.Create(position, radius));
    }

    private static Vector2 CalculateOccupiedCellCenter(int gridWidth, int gridHeight)
    {
        var bounds = CalculateOccupiedCellBounds(gridWidth, gridHeight);
        return new Vector2((bounds.MinX + bounds.MaxX) * 0.5f, (bounds.MinZ + bounds.MaxZ) * 0.5f);
    }

    private static OccupiedCellBounds CalculateOccupiedCellBounds(int gridWidth, int gridHeight)
    {
        gridWidth = Math.Clamp(gridWidth, 1, GridColumns);
        gridHeight = Math.Clamp(gridHeight, 1, GridRows);
        var startColumn = (GridColumns - gridWidth) / 2;
        var startRow = (GridRows - gridHeight) / 2;
        var minX = -GridColumns * GridCellWorldSize * 0.5f + startColumn * GridCellWorldSize;
        var maxX = minX + gridWidth * GridCellWorldSize;
        var minZ = -GridRows * GridCellWorldSize * 0.5f + startRow * GridCellWorldSize;
        var maxZ = minZ + gridHeight * GridCellWorldSize;
        return new OccupiedCellBounds(minX, minZ, maxX, maxZ);
    }

    private ModelGpuMesh UploadMesh(Mesh mesh)
    {
        var vertexBytes = MemoryMarshal.AsBytes(mesh.Vertices.AsSpan());
        var indexBytes = MemoryMarshal.AsBytes(mesh.Indices.AsSpan());
        var vertexBuffer = CreateUploadBuffer(vertexBytes);
        var indexBuffer = CreateUploadBuffer(indexBytes);
        return new ModelGpuMesh(
            vertexBuffer,
            indexBuffer,
            new VertexBufferView(vertexBuffer.GPUVirtualAddress, (uint)vertexBytes.Length, (uint)VertexStride),
            new IndexBufferView(indexBuffer.GPUVirtualAddress, (uint)indexBytes.Length, Format.R16_UInt),
            mesh.Indices.Length);
    }

    private unsafe ID3D12Resource CreateUploadBuffer(ReadOnlySpan<byte> bytes)
    {
        var description = new ResourceDescription(ResourceDimension.Buffer, 0, (ulong)bytes.Length, 1, 1, 1, Format.Unknown, 1, 0, TextureLayout.RowMajor, ResourceFlags.None);
        var resource = CreateCommittedResource(HeapType.Upload, description, ResourceStates.GenericRead);

        void* mapped;
        resource.Map(0, null, &mapped).CheckError();
        try
        {
            fixed (byte* source = bytes)
                Buffer.MemoryCopy(source, mapped, bytes.Length, bytes.Length);
        }
        finally
        {
            resource.Unmap(0, null);
        }

        return resource;
    }

    private ID3D12Resource CreateCommittedResource(HeapType heapType, ResourceDescription description, ResourceStates initialState)
    {
        var heapProperties = new HeapProperties(heapType, 0, 0);
        return _device.CreateCommittedResource(heapProperties, HeapFlags.None, description, initialState, null);
    }

    private static void Transition(ID3D12GraphicsCommandList commandList, ID3D12Resource resource, ResourceStates before, ResourceStates after)
    {
        var barrier = ResourceBarrier.BarrierTransition(resource, before, after, uint.MaxValue, ResourceBarrierFlags.None);
        commandList.ResourceBarrier([barrier]);
    }

    private void Transition(ID3D12Resource resource, ResourceStates before, ResourceStates after) => Transition(_commandList, resource, before, after);

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
        Win32Native.WaitForSingleObject(_fenceEvent, uint.MaxValue);
        _commandsInFlight = false;
    }

    private void DisposeModelTextures()
    {
        foreach (var texture in _textures.Values)
        {
            texture.Resource.Dispose();
            texture.OverlayResource?.Dispose();
        }

        _textures.Clear();
    }

    private void GetClientSize(out int width, out int height)
    {
        if (!Win32Native.GetClientRect(_hwnd, out var rect))
        {
            width = 1;
            height = 1;
            return;
        }

        width = Math.Max(1, rect.Right - rect.Left);
        height = Math.Max(1, rect.Bottom - rect.Top);
    }

    private CpuDescriptorHandle RtvHandle(int index) => _rtvHeap.GetCPUDescriptorHandleForHeapStart() + index * _rtvDescriptorSize;

    private CpuDescriptorHandle DsvHandle() => _dsvHeap.GetCPUDescriptorHandleForHeapStart();

    private CpuDescriptorHandle SrvCpuHandle(int index) => _srvHeap.GetCPUDescriptorHandleForHeapStart() + index * _srvDescriptorSize;

    private GpuDescriptorHandle SrvGpuHandle(int index) => _srvHeap.GetGPUDescriptorHandleForHeapStart() + index * _srvDescriptorSize;

    private static bool HasTranslucentPixels(ReadOnlySpan<byte> rgba8)
    {
        for (var index = 3; index < rgba8.Length; index += 4)
        {
            var alpha = rgba8[index];
            if (alpha is not 0 and not byte.MaxValue)
                return true;
        }

        return false;
    }

    private static MeshBounds CalculateBounds(IReadOnlyList<VertexPositionNormalTexture> vertices)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var vertex in vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }

        var center = (min + max) * 0.5f;
        var radius = vertices.Count == 0 ? 1.0f : vertices.Max(vertex => Vector3.Distance(vertex.Position, center));
        return new MeshBounds(min, max, center, Math.Max(1.0f, radius));
    }

    private static float CalculateGridFitScale(
        IReadOnlyList<VertexPositionNormalTexture> vertices,
        Vector3 pivot,
        Matrix4x4 rotation,
        int gridWidth,
        int gridHeight)
    {
        if (vertices.Count == 0)
            return 1.0f;

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var vertex in vertices)
        {
            var position = Vector3.Transform(vertex.Position - pivot, rotation);
            min = Vector2.Min(min, new Vector2(position.X, position.Z));
            max = Vector2.Max(max, new Vector2(position.X, position.Z));
        }

        var extents = Vector2.Max(max - min, new Vector2(0.001f));
        var target = new Vector2(
            Math.Max(1, gridWidth) * GridCellWorldSize * GridFitPadding,
            Math.Max(1, gridHeight) * GridCellWorldSize * GridFitPadding);
        var scale = Math.Min(target.X / extents.X, target.Y / extents.Y);
        return float.IsFinite(scale) && scale > 0.0f ? scale : 1.0f;
    }

    private unsafe void WritePreviewSceneConstants(float* target)
    {
        var sceneRadius = Math.Max(GridCellWorldSize * 3.0f, _meshBounds.Radius * _modelScale * 2.0f);
        var distance = Math.Max(120.0f, sceneRadius * 3.25f);

        _modelShaderConstants.WriteSceneConstants(
            target,
            new Vector3(-sceneRadius * 0.35f, -distance * 0.55f, sceneRadius * 0.85f),
            specularIntensity: 0.10f,
            new Vector3(0.0f, -distance, sceneRadius * 0.18f),
            shininess: 16.0f,
            new Vector4(1.0f, 1.0f, 1.0f, 0.38f),
            new Vector4(1.0f, 0.96f, 0.88f, 0.82f),
            new Vector4(203.0f, 203.0f, 300.0f, 80.0f));
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               float.IsFinite(value.Z);
    }

    private enum ModelSurfacePass
    {
        Static = 0,
        AnimatedBase = 1,
        EffectOverlay = 2
    }

    private sealed record ModelTexture(
        ID3D12Resource Resource,
        int SrvSlot,
        TextureAnimation Animation,
        ID3D12Resource? OverlayResource,
        int OverlaySrvSlot,
        TextureAnimation OverlayAnimation,
        TextureOverlayMode OverlayMode,
        bool HasTranslucentPixels);

    private sealed record UploadedModelTexture(
        string Name,
        ID3D12Resource Resource,
        int SrvSlot,
        TextureAnimation Animation,
        ID3D12Resource? OverlayResource,
        int OverlaySrvSlot,
        TextureAnimation OverlayAnimation,
        TextureOverlayMode OverlayMode,
        bool HasTranslucentPixels);

    private readonly record struct InventoryUiSurface(int IndexStart, int IndexCount, Vector4 Color);

    private readonly record struct OccupiedCellBounds(float MinX, float MinZ, float MaxX, float MaxZ);

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

    private readonly record struct MeshBounds(Vector3 Min, Vector3 Max, Vector3 Center, float Radius);
}
