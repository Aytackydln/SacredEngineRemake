using System;
using System.Threading;
using Sacred.Engine.Extern;
using Sacred.Shaders;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Swapchain;

internal abstract class Dx12SwapChain : IDisposable
{
    private const uint FrameLatencyPollMilliseconds = 50;
    private readonly nint _frameLatencyWaitableObject;

    private protected Dx12SwapChain(IDXGISwapChain3 swapChain)
    {
        SwapChain = swapChain;
        using var swapChain2 = SwapChain.QueryInterface<IDXGISwapChain2>();
        swapChain2.MaximumFrameLatency = 1;
        _frameLatencyWaitableObject = swapChain2.FrameLatencyWaitableObject;
        if (_frameLatencyWaitableObject == 0)
            throw new InvalidOperationException("DXGI did not provide a frame-latency waitable object.");
    }

    public IDXGISwapChain3 SwapChain { get; }

    public abstract Format BackBufferFormat { get; }

    public abstract ColorSpaceType ColorSpace { get; }

    public abstract Dx12ShaderSet Shaders { get; }

    public uint CurrentBackBufferIndex => SwapChain.CurrentBackBufferIndex;

    public void WaitForPresentSlot(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = Kernel32.WaitForSingleObject(_frameLatencyWaitableObject, FrameLatencyPollMilliseconds);
            if (result == Kernel32.WaitObject0)
                return;
            if (result != Kernel32.WaitTimeout)
                throw new InvalidOperationException("Failed while waiting for an available DXGI presentation slot.");
        }
    }

    public void Present(bool verticalSyncEnabled, bool allowTearing)
    {
        if (verticalSyncEnabled)
        {
            SwapChain.Present(1, PresentFlags.None);
            return;
        }

        SwapChain.Present(0, allowTearing ? PresentFlags.AllowTearing : PresentFlags.None);
    }

    public void ResizeBuffers(int frameCount, int width, int height, SwapChainFlags flags)
    {
        SwapChain.ResizeBuffers((uint)frameCount, (uint)width, (uint)height, BackBufferFormat, flags).CheckError();
        ApplyColorSpace();
    }

    public ID3D12Resource GetBuffer(uint index) => SwapChain.GetBuffer<ID3D12Resource>(index);

    public void Dispose() => SwapChain.Dispose();

    private protected void ApplyColorSpace() => SwapChain.SetColorSpace1(ColorSpace);

    private protected static IDXGISwapChain3 CreateNativeSwapChain(
        IDXGIFactory2 factory,
        ID3D12CommandQueue commandQueue,
        nint hwnd,
        int width,
        int height,
        int frameCount,
        Format format,
        SwapChainFlags flags)
    {
        var description = new SwapChainDescription1(
            (uint)width,
            (uint)height,
            format,
            false,
            Usage.RenderTargetOutput,
            (uint)frameCount,
            Scaling.Stretch,
            SwapEffect.FlipDiscard,
            AlphaMode.Ignore,
            flags);

        using var swapChain1 = factory.CreateSwapChainForHwnd(commandQueue, hwnd, description, null, null);
        return swapChain1.QueryInterface<IDXGISwapChain3>();
    }
}
