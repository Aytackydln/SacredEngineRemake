using System;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Swapchain;

internal abstract class Dx12SwapChain : IDisposable
{
    private protected Dx12SwapChain(IDXGISwapChain3 swapChain)
    {
        SwapChain = swapChain;
    }

    public IDXGISwapChain3 SwapChain { get; }

    public abstract Format BackBufferFormat { get; }

    public abstract ColorSpaceType ColorSpace { get; }

    public abstract Dx12ShaderSet Shaders { get; }

    public abstract Dx12DisplayProfile DisplayProfile { get; }

    public uint CurrentBackBufferIndex => SwapChain.CurrentBackBufferIndex;

    public void Present() => SwapChain.Present(0, PresentFlags.None);

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