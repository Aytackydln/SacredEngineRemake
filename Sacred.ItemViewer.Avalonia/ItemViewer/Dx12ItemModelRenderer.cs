using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Assets;
using Sacred.Assets.Paks.Texture;
using Sacred.Granny;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

internal sealed unsafe class Dx12ItemModelRenderer : IDisposable
{
    private const int FrameCount = 2;
    private const int MaxTextures = 128;
    private const int FallbackTextureSlot = 0;
    private const int FirstModelTextureSlot = 1;
    private const int ModelRootConstantCount = 40;
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
    private const Format Rgba8Format = Format.R8G8B8A8_UNorm;

    private readonly nint _hwnd;
    private readonly ID3D12Resource[] _backBuffers = new ID3D12Resource[FrameCount];
    private readonly Dictionary<string, ModelTexture> _textures = new(StringComparer.OrdinalIgnoreCase);

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
    private ID3D12PipelineState _inventoryUiPipelineState = null!;
    private ID3D12Resource? _depthBuffer;
    private ID3D12Resource? _fallbackTexture;
    private ModelGpuMesh? _inventoryUiMesh;
    private ModelGpuMesh? _mesh;
    private InventoryUiSurface[] _inventoryUiSurfaces = [];
    private MeshBounds _meshBounds = new(Vector3.Zero, Vector3.Zero, Vector3.Zero, 1.0f);
    private MeshSurface[] _surfaces = [];
    private string _modelName = string.Empty;
    private Vector3 _previewRotation;
    private ItemPreviewRotationMode _rotationMode;
    private ItemPreviewPivotMode _pivotMode;
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
    private ulong _fenceValue;
    private bool _commandsInFlight;
    private bool _disposed;

    public Dx12ItemModelRenderer(nint hwnd)
    {
        _hwnd = hwnd;
        CreateDevice();
        CreateSwapChain();
        CreateDescriptorHeaps();
        CreateBackBuffers();
        CreateDepthBuffer();
        CreateCommands();
        CreatePipeline();
        RebuildInventoryUiMesh(includeOccupiedCells: false);
        CreateFallbackTexture();
    }

    public void ClearModel()
    {
        WaitForGpu();
        _mesh?.Dispose();
        _mesh = null;
        _surfaces = [];
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
        ItemPreviewPivotMode pivotMode)
    {
        WaitForGpu();
        _mesh?.Dispose();
        _mesh = null;
        _surfaces = [];
        _modelName = asset.Name;
        _previewRotation = IsFinite(previewRotation) ? previewRotation : Vector3.Zero;
        _rotationMode = rotationMode;
        _pivotMode = pivotMode;
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

    public void SetTextures(IReadOnlyDictionary<string, TextureAsset> textures)
    {
        WaitForGpu();
        DisposeModelTextures();

        var slot = FirstModelTextureSlot;
        foreach (var pair in textures)
        {
            if (slot >= MaxTextures)
                break;

            var resource = UploadTextureAndWait(pair.Value.Width, pair.Value.Height, pair.Value.Rgba8);
            _device.CreateShaderResourceView(resource, null, SrvCpuHandle(slot));
            _textures[pair.Key] = new ModelTexture(resource, slot);
            slot++;
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
        WaitForGpu();
        ClearModel();

        _fallbackTexture?.Dispose();
        _fallbackTexture = null;
        _inventoryUiMesh?.Dispose();
        _inventoryUiMesh = null;
        _depthBuffer?.Dispose();
        _depthBuffer = null;

        foreach (var backBuffer in _backBuffers)
            backBuffer?.Dispose();

        _inventoryUiPipelineState.Dispose();
        _pipelineState.Dispose();
        _rootSignature.Dispose();
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
        var rootParameters = new[]
        {
            new RootParameter(new RootConstants(0, 0, ModelRootConstantCount), ShaderVisibility.All),
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
                StaticBorderColor.OpaqueWhite,
                0.0f,
                float.MaxValue,
                ShaderVisibility.Pixel,
                0)
        };

        var rootDescription = new RootSignatureDescription(RootSignatureFlags.AllowInputAssemblerInputLayout, rootParameters, samplers);
        _rootSignature = _device.CreateRootSignature(in rootDescription, RootSignatureVersion.Version1);

        var shaderSource = EmbeddedResource_Shaders.SacredModel_hlsl.ReadAllText();
        var vertexShader = D3DShaderCompiler.Compile("SacredItemViewer", shaderSource, "vs_main", "vs_5_0");
        var pixelShader = D3DShaderCompiler.Compile("SacredItemViewer", shaderSource, "ps_main", "ps_5_0");
        var depthStencil = DepthStencilDescription.Default;
        depthStencil.DepthFunc = ComparisonFunction.LessEqual;
        var inputLayout = new[]
        {
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 24, 0)
        };

        var pipelineDescription = new GraphicsPipelineStateDescription
        {
            RootSignature = _rootSignature,
            VertexShader = vertexShader,
            PixelShader = pixelShader,
            InputLayout = inputLayout,
            BlendState = BlendDescription.AlphaBlend,
            RasterizerState = RasterizerDescription.CullClockwise,
            DepthStencilState = depthStencil,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [BackBufferFormat],
            DepthStencilFormat = DepthBufferFormat,
            SampleDescription = new SampleDescription(1, 0)
        };

        _pipelineState = _device.CreateGraphicsPipelineState(pipelineDescription);

        var inventoryUiShaderSource = EmbeddedResource_Shaders.SacredInventoryUi_hlsl.ReadAllText();
        var inventoryUiVertexShader = D3DShaderCompiler.Compile("SacredInventoryUi", inventoryUiShaderSource, "vs_main", "vs_5_0");
        var inventoryUiPixelShader = D3DShaderCompiler.Compile("SacredInventoryUi", inventoryUiShaderSource, "ps_main", "ps_5_0");
        var inventoryUiDepthStencil = DepthStencilDescription.Default;
        inventoryUiDepthStencil.DepthEnable = false;
        inventoryUiDepthStencil.DepthWriteMask = DepthWriteMask.Zero;

        pipelineDescription.VertexShader = inventoryUiVertexShader;
        pipelineDescription.PixelShader = inventoryUiPixelShader;
        pipelineDescription.RasterizerState = RasterizerDescription.CullNone;
        pipelineDescription.DepthStencilState = inventoryUiDepthStencil;
        _inventoryUiPipelineState = _device.CreateGraphicsPipelineState(pipelineDescription);
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
        _fallbackTexture = UploadTextureAndWait(2, 2, rgba);
        _device.CreateShaderResourceView(_fallbackTexture, null, SrvCpuHandle(FallbackTextureSlot));
    }

    private void ResizeIfNeeded()
    {
        GetClientSize(out var width, out var height);
        if (width == _renderWidth && height == _renderHeight)
            return;

        foreach (var backBuffer in _backBuffers)
            backBuffer.Dispose();

        _swapChain.ResizeBuffers(FrameCount, (uint)width, (uint)height, BackBufferFormat, SwapChainFlags.None).CheckError();
        _renderWidth = width;
        _renderHeight = height;
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
            RecordModel();

        Transition(backBuffer, ResourceStates.RenderTarget, ResourceStates.Present);
    }

    private void RecordModel()
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

        var constants = stackalloc float[ModelRootConstantCount];
        var world = CreateWorldMatrix();
        var wvp = world * CreateViewProjectionMatrix();
        WriteMatrix(wvp, constants);
        WriteMatrix(world, constants + 16);
        WriteModelColor(_modelName, constants + 32);
        constants[37] = 0.0f;
        constants[38] = 0.5f;
        constants[39] = 0.10f;

        foreach (var surface in _surfaces)
        {
            if (surface.IndexCount <= 0 || surface.IndexStart >= mesh.IndexCount)
                continue;

            var drawCount = Math.Min(surface.IndexCount, mesh.IndexCount - surface.IndexStart);
            var texture = ResolveTexture(surface.TextureName);
            constants[36] = texture is null ? 0.0f : 1.0f;
            _commandList.SetGraphicsRoot32BitConstants(0, ModelRootConstantCount, constants, 0);
            _commandList.SetGraphicsRootDescriptorTable(1, SrvGpuHandle(texture?.SrvSlot ?? FallbackTextureSlot));
            _commandList.DrawIndexedInstanced((uint)drawCount, 1, (uint)surface.IndexStart, 0, 0);
        }
    }

    private void RecordInventoryUi()
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

        var constants = stackalloc float[ModelRootConstantCount];
        var world = CreateGridWorldMatrix();
        var wvp = world * CreateViewProjectionMatrix();
        WriteMatrix(wvp, constants);
        WriteMatrix(world, constants + 16);

        foreach (var surface in _inventoryUiSurfaces)
        {
            WriteColor(surface.Color, constants + 32);
            constants[36] = 0.0f;
            constants[37] = 0.0f;
            constants[38] = 0.0f;
            constants[39] = 0.0f;
            _commandList.SetGraphicsRoot32BitConstants(0, ModelRootConstantCount, constants, 0);
            _commandList.DrawIndexedInstanced((uint)surface.IndexCount, 1, (uint)surface.IndexStart, 0, 0);
        }
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
            ItemPreviewRotationMode.RawXyz => Matrix4x4.CreateRotationX(rotation.X) *
                                             Matrix4x4.CreateRotationY(rotation.Y) *
                                             Matrix4x4.CreateRotationZ(rotation.Z),
            ItemPreviewRotationMode.DirectYawPitchRoll => Matrix4x4.CreateFromYawPitchRoll(rotation.X, rotation.Y, rotation.Z),
            ItemPreviewRotationMode.GrnMatrix => CreateGrnRotationMatrix(rotation),
            _ => Matrix4x4.CreateFromYawPitchRoll(rotation.Y, rotation.Z, rotation.X)
        };
    }

    private Vector3 GetPivotPoint()
    {
        return _pivotMode switch
        {
            ItemPreviewPivotMode.ModelOrigin => Vector3.Zero,
            ItemPreviewPivotMode.BoundsBottomCenter => new Vector3(_meshBounds.Center.X, _meshBounds.Center.Y, _meshBounds.Min.Z),
            ItemPreviewPivotMode.BoundsTopCenter => new Vector3(_meshBounds.Center.X, _meshBounds.Center.Y, _meshBounds.Max.Z),
            ItemPreviewPivotMode.BoundsCenterGround => new Vector3(_meshBounds.Center.X, _meshBounds.Center.Y, 0.0f),
            _ => _meshBounds.Center
        };
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

    private ID3D12Resource CreateUploadBuffer(ReadOnlySpan<byte> bytes)
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

    private ID3D12Resource UploadTextureAndWait(int width, int height, byte[] rgba)
    {
        ID3D12Resource? texture = null;
        ID3D12Resource? upload = null;
        try
        {
            texture = CreateTexture2D(width, height, Rgba8Format, ResourceStates.CopyDest);
            upload = CreateRgbaUploadBuffer(width, height, rgba);

            _commandAllocator.Reset();
            _commandList.Reset(_commandAllocator, null);
            CopyUploadToTexture(_commandList, upload, texture, width, height);
            Transition(_commandList, texture, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
            _commandList.Close();
            _commandQueue.ExecuteCommandLists([_commandList]);
            SignalFrameFence();
            WaitForGpu();

            upload.Dispose();
            upload = null;
            return texture;
        }
        catch
        {
            upload?.Dispose();
            texture?.Dispose();
            throw;
        }
    }

    private ID3D12Resource CreateRgbaUploadBuffer(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Texture dimensions must be positive.");

        var rowBytes = width * 4;
        var requiredBytes = rowBytes * height;
        if (rgba.Length < requiredBytes)
            throw new ArgumentException($"RGBA buffer is too small for {width}x{height} texture.", nameof(rgba));

        var rowPitch = Align(rowBytes, 256);
        var uploadSize = (ulong)(rowPitch * height);
        var uploadDescription = new ResourceDescription(ResourceDimension.Buffer, 0, uploadSize, 1, 1, 1, Format.Unknown, 1, 0, TextureLayout.RowMajor, ResourceFlags.None);
        var upload = CreateCommittedResource(HeapType.Upload, uploadDescription, ResourceStates.GenericRead);

        void* mapped;
        upload.Map(0, null, &mapped).CheckError();
        try
        {
            for (var y = 0; y < height; y++)
            {
                var sourceOffset = y * rowBytes;
                var destination = IntPtr.Add((nint)mapped, y * rowPitch);
                Marshal.Copy(rgba, sourceOffset, destination, rowBytes);
            }
        }
        finally
        {
            upload.Unmap(0, null);
        }

        return upload;
    }

    private ID3D12Resource CreateCommittedResource(HeapType heapType, ResourceDescription description, ResourceStates initialState)
    {
        var heapProperties = new HeapProperties(heapType, 0, 0);
        return _device.CreateCommittedResource(heapProperties, HeapFlags.None, description, initialState, null);
    }

    private ID3D12Resource CreateTexture2D(int width, int height, Format format, ResourceStates initialState)
    {
        var description = new ResourceDescription(ResourceDimension.Texture2D, 0, (ulong)width, (uint)height, 1, 1, format, 1, 0, TextureLayout.Unknown, ResourceFlags.None);
        return CreateCommittedResource(HeapType.Default, description, initialState);
    }

    private static void CopyUploadToTexture(ID3D12GraphicsCommandList commandList, ID3D12Resource upload, ID3D12Resource texture, int width, int height)
    {
        var rowPitch = Align(width * 4, 256);
        var footprint = new PlacedSubresourceFootPrint
        {
            Offset = 0,
            Footprint = new SubresourceFootPrint(Rgba8Format, (uint)width, (uint)height, 1, (uint)rowPitch)
        };

        commandList.CopyTextureRegion(new TextureCopyLocation(texture, 0), 0, 0, 0, new TextureCopyLocation(upload, footprint), null);
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
            texture.Resource.Dispose();
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

    private static void WriteMatrix(Matrix4x4 matrix, float* target)
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

    private static void WriteModelColor(string name, float* target)
    {
        var hash = (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(name);
        target[0] = 0.35f + ((hash & 0xFF) / 255.0f) * 0.55f;
        target[1] = 0.35f + (((hash >> 8) & 0xFF) / 255.0f) * 0.55f;
        target[2] = 0.35f + (((hash >> 16) & 0xFF) / 255.0f) * 0.55f;
        target[3] = 1.0f;
    }

    private static void WriteColor(Vector4 color, float* target)
    {
        target[0] = color.X;
        target[1] = color.Y;
        target[2] = color.Z;
        target[3] = color.W;
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
               float.IsFinite(value.Y) &&
               float.IsFinite(value.Z);
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);

    private sealed record ModelTexture(ID3D12Resource Resource, int SrvSlot);

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
