using System;
using System.Diagnostics;
using System.Threading;
using Sacred.Engine.Extern;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.Swapchain;
using Sacred.Engine.Latency;
using Sacred.Engine.Platform;
using Sacred.Shaders;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace Sacred.Engine.Graphics;

/// <summary>Owns the D3D12 device, swap chain, descriptors, frame contexts, and GPU synchronization.</summary>
internal sealed class Dx12DeviceContext : IDisposable
{
    public const int FrameCount = 2;
    public const Format DepthBufferFormat = Format.D32_Float;
    private const FeatureLevel MinimumFeatureLevel = FeatureLevel.Level_11_0;

    private static readonly TimeSpan ResizeDebounce = TimeSpan.FromMilliseconds(150);

    private readonly Win32Window _window;
    private readonly LowLatencySystem _latency;
    private readonly ID3D12Resource[] _backBuffers = new ID3D12Resource[FrameCount];
    private readonly ID3D12CommandList[] _submittedCommandLists = new ID3D12CommandList[1];
    private readonly ID3D12DescriptorHeap[] _shaderVisibleDescriptorHeaps = new ID3D12DescriptorHeap[1];

    private IDXGIFactory2 _factory = null!;
    private ID3D12Device _device = null!;
    private ID3D12CommandQueue _commandQueue = null!;
    private Dx12SwapChain _swapChain = null!;
    private ID3D12DescriptorHeap _rtvHeap = null!;
    private ID3D12DescriptorHeap _dsvHeap = null!;
    private ID3D12DescriptorHeap _srvHeap = null!;
    private ID3D12GraphicsCommandList _commandList = null!;
    private ID3D12Fence _fence = null!;
    private ID3D12Resource? _depthBuffer;
    private Dx12FrameContext[] _frames = null!;
    private Dx12FrameContext? _currentFrame;

    private nint _fenceEvent;
    private int _rtvDescriptorSize;
    private int _srvDescriptorSize;
    private int _renderWidth;
    private int _renderHeight;
    private int _pendingRenderWidth;
    private int _pendingRenderHeight;
    private ulong _fenceValue;
    private Dx12SwapChainMode _requestedSwapChainMode;
    private SwapChainFlags _swapChainFlags;
    private bool _allowTearing;
    private bool _submissionOpen;
    private long _lastResizeRequestTimestamp;
    private HdrBrightnessSettings _hdrBrightnessSettings;

    public Dx12DeviceContext(
        Win32Window window,
        LowLatencySystem latency,
        int srvDescriptorCount,
        bool hdrEnabled,
        HdrBrightnessSettings hdrBrightnessSettings)
    {
        _window = window;
        _latency = latency;
        _hdrBrightnessSettings = hdrBrightnessSettings.Normalized();
        _requestedSwapChainMode = hdrEnabled ? Dx12SwapChainMode.Hdr : Dx12SwapChainMode.Sdr;
        CreateDevice();
        CreateSwapChain();
        CreateDescriptorHeaps(srvDescriptorCount);
        CreateBackBuffers();
        CreateDepthBuffer();
        CreateCommands();
    }

    public ID3D12Device Device => _device;
    public ID3D12GraphicsCommandList CommandList => _commandList;
    public ID3D12DescriptorHeap SrvHeap => _srvHeap;
    public ID3D12DescriptorHeap[] ShaderVisibleDescriptorHeaps => _shaderVisibleDescriptorHeaps;
    public int SrvDescriptorSize => _srvDescriptorSize;
    public int RenderWidth => _renderWidth;
    public int RenderHeight => _renderHeight;
    public bool VariableRefreshRateSupported => _allowTearing;
    public bool IsHdrEnabled => _swapChain is Dx12HdrSwapChain;
    public Format BackBufferFormat => _swapChain.BackBufferFormat;
    public Dx12ShaderSet Shaders => _swapChain.Shaders;
    public HdrBrightnessSettings HdrBrightnessSettings => _hdrBrightnessSettings;
    public Dx12DisplayProfile DisplayProfile => IsHdrEnabled
        ? Dx12DisplayProfile.CreateHdr(_hdrBrightnessSettings)
        : Dx12DisplayProfile.Sdr;

    public void SetHdrBrightnessSettings(HdrBrightnessSettings settings) =>
        _hdrBrightnessSettings = settings.Normalized();

    public Dx12FrameContext CurrentFrame =>
        _currentFrame ?? throw new InvalidOperationException("No Direct3D frame is being recorded.");

    public ID3D12Resource CurrentBackBuffer => _backBuffers[_swapChain.CurrentBackBufferIndex];
    public CpuDescriptorHandle CurrentRenderTarget => RtvHandle((int)_swapChain.CurrentBackBufferIndex);
    public CpuDescriptorHandle DepthStencil => _dsvHeap.GetCPUDescriptorHandleForHeapStart();

    public CpuDescriptorHandle SrvCpuHandle(int index) =>
        _srvHeap.GetCPUDescriptorHandleForHeapStart() + index * _srvDescriptorSize;

    public GpuDescriptorHandle SrvGpuHandle(int index) =>
        _srvHeap.GetGPUDescriptorHandleForHeapStart() + index * _srvDescriptorSize;

    public void AcquireFrame(CancellationToken cancellationToken, Action<Dx12FrameContext> releaseRetiredResources)
    {
        if (_currentFrame is not null)
            throw new InvalidOperationException("The previous Direct3D frame was not submitted.");

        ResizeIfNeeded(releaseRetiredResources);
        _swapChain.WaitForPresentSlot(cancellationToken);

        var frame = _frames[_swapChain.CurrentBackBufferIndex];
        var fenceValue = frame.FenceValue;
        if (fenceValue != 0 && _fence.CompletedValue < fenceValue)
        {
            _fence.SetEventOnCompletion(fenceValue, _fenceEvent).CheckError();
            WaitForFence(cancellationToken);
        }

        releaseRetiredResources(frame);
        _currentFrame = frame;
    }

    public void BeginRenderSubmission(ID3D12PipelineState initialPipeline)
    {
        if (_submissionOpen)
            throw new InvalidOperationException("A Direct3D render submission is already open.");

        _submissionOpen = true;
        CurrentFrame.CommandAllocator.Reset();
        _commandList.Reset(CurrentFrame.CommandAllocator, initialPipeline);
    }

    public Dx12PendingScreenshot? SubmitAndPresent(
        bool verticalSyncEnabled,
        ulong frameId,
        bool captureScreenshot)
    {
        if (!_submissionOpen)
            throw new InvalidOperationException("No Direct3D render submission is open.");

        var capture = captureScreenshot
            ? Dx12BackBufferCapture.Record(
                _device,
                _commandList,
                CurrentBackBuffer,
                _renderWidth,
                _renderHeight,
                BackBufferFormat,
                _swapChain.ColorSpace)
            : null;
        _commandList.Close();
        _latency.Mark(LatencyMarker.RenderSubmitStart, frameId);
        _commandQueue.ExecuteCommandLists(1, _submittedCommandLists);
        _latency.Mark(LatencyMarker.RenderSubmitEnd, frameId);

        var fenceValue = ++_fenceValue;
        _commandQueue.Signal(_fence, fenceValue).CheckError();
        CurrentFrame.FenceValue = fenceValue;

        _latency.Mark(LatencyMarker.PresentStart, frameId);
        _swapChain.Present(verticalSyncEnabled, _allowTearing);
        _latency.Mark(LatencyMarker.PresentEnd, frameId);
        _submissionOpen = false;
        _currentFrame = null;

        return capture is null
            ? null
            : new Dx12PendingScreenshot(capture, _fence, fenceValue);
    }

    public void WaitForGpu(Action<Dx12FrameContext> releaseRetiredResources)
    {
        if (_fenceValue != 0 && _fence.CompletedValue < _fenceValue)
        {
            _fence.SetEventOnCompletion(_fenceValue, _fenceEvent).CheckError();
            Kernel32.WaitForSingleObject(_fenceEvent, Kernel32.Infinite);
        }

        foreach (var frame in _frames)
            releaseRetiredResources(frame);
    }

    public void RecreateSwapChain(Dx12SwapChainMode requestedMode)
    {
        DisposeBackBuffers();
        _depthBuffer?.Dispose();
        _depthBuffer = null;
        _swapChain.Dispose();

        _requestedSwapChainMode = requestedMode;
        CreateSwapChain();
        CreateBackBuffers();
        CreateDepthBuffer();
    }

    public void Dispose()
    {
        _depthBuffer?.Dispose();
        _depthBuffer = null;
        DisposeBackBuffers();
        _fence.Dispose();
        _commandList.Dispose();
        foreach (var frame in _frames)
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
        using var factory5 = _factory.QueryInterfaceOrNull<IDXGIFactory5>();
        _allowTearing = factory5?.PresentAllowTearing == true;
        _swapChainFlags = SwapChainFlags.FrameLatencyWaitableObject;
        if (_allowTearing)
            _swapChainFlags |= SwapChainFlags.AllowTearing;

        // This is a minimum, not a cap on the device's available features. Requiring
        // 12.2 rejects older D3D12 runtimes and GPUs even though the engine uses the
        // feature-level 11 shader/resource baseline.
        _device = D3D12CreateDevice<ID3D12Device>(null, MinimumFeatureLevel);
        _commandQueue = _device.CreateCommandQueue(CommandListType.Direct);
        _latency.AttachD3D12(_device.NativePointer, _commandQueue.NativePointer);
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
        _requestedSwapChainMode = IsHdrEnabled ? Dx12SwapChainMode.Hdr : Dx12SwapChainMode.Sdr;
    }

    private void CreateDescriptorHeaps(int srvDescriptorCount)
    {
        _rtvHeap = CreateDescriptorHeap(DescriptorHeapType.RenderTargetView, FrameCount, DescriptorHeapFlags.None);
        _dsvHeap = CreateDescriptorHeap(DescriptorHeapType.DepthStencilView, 1, DescriptorHeapFlags.None);
        _srvHeap = CreateDescriptorHeap(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            srvDescriptorCount,
            DescriptorHeapFlags.ShaderVisible);
        _shaderVisibleDescriptorHeaps[0] = _srvHeap;
        _rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        _srvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
    }

    private void CreateBackBuffers()
    {
        for (var index = 0; index < FrameCount; index++)
        {
            _backBuffers[index] = _swapChain.GetBuffer((uint)index);
            _device.CreateRenderTargetView(_backBuffers[index], null, RtvHandle(index));
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
        _depthBuffer = _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default, 0, 0),
            HeapFlags.None,
            description,
            ResourceStates.DepthWrite,
            clearValue);
        _device.CreateDepthStencilView(_depthBuffer, null, DepthStencil);
    }

    private void CreateCommands()
    {
        _frames = new Dx12FrameContext[FrameCount];
        for (var index = 0; index < _frames.Length; index++)
            _frames[index] = new Dx12FrameContext(index, _device.CreateCommandAllocator(CommandListType.Direct));

        _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(
            CommandListType.Direct,
            _frames[0].CommandAllocator,
            null);
        _submittedCommandLists[0] = _commandList;
        _commandList.Close();
        _fence = _device.CreateFence(0, FenceFlags.None);
        _fenceEvent = Kernel32.CreateEventA(IntPtr.Zero, false, false, null);
        if (_fenceEvent == 0)
            throw new InvalidOperationException("Failed to create D3D12 fence event.");
    }

    private void ResizeIfNeeded(Action<Dx12FrameContext> releaseRetiredResources)
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

        WaitForGpu(releaseRetiredResources);
        DisposeBackBuffers();
        _swapChain.ResizeBuffers(FrameCount, _pendingRenderWidth, _pendingRenderHeight, _swapChainFlags);
        _renderWidth = _pendingRenderWidth;
        _renderHeight = _pendingRenderHeight;
        _pendingRenderWidth = 0;
        _pendingRenderHeight = 0;
        CreateBackBuffers();
        CreateDepthBuffer();
    }

    private void WaitForFence(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = Kernel32.WaitForSingleObject(_fenceEvent, 50);
            if (result == Kernel32.WaitObject0)
                return;
            if (result != Kernel32.WaitTimeout)
                throw new InvalidOperationException("Failed while waiting for a Direct3D frame fence.");
        }
    }

    private ID3D12DescriptorHeap CreateDescriptorHeap(
        DescriptorHeapType type,
        int count,
        DescriptorHeapFlags flags) =>
        _device.CreateDescriptorHeap(new DescriptorHeapDescription(type, (uint)count, flags, 0));

    private CpuDescriptorHandle RtvHandle(int index) =>
        _rtvHeap.GetCPUDescriptorHandleForHeapStart() + index * _rtvDescriptorSize;

    private void DisposeBackBuffers()
    {
        for (var index = 0; index < _backBuffers.Length; index++)
        {
            _backBuffers[index]?.Dispose();
            _backBuffers[index] = null!;
        }
    }
}
