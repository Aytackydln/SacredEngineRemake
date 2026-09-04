using System;
using Sacred.Shaders;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Swapchain;

internal sealed class Dx12HdrSwapChain : Dx12SwapChain
{
    public const Format HdrBackBufferFormat = Format.R10G10B10A2_UNorm;
    public const ColorSpaceType HdrColorSpace = ColorSpaceType.RgbFullG2084NoneP2020;

    public Dx12HdrSwapChain(
        IDXGIFactory2 factory,
        ID3D12CommandQueue commandQueue,
        nint hwnd,
        int width,
        int height,
        int frameCount,
        SwapChainFlags flags)
        : base(CreateHdrNativeSwapChain(factory, commandQueue, hwnd, width, height, frameCount, flags))
    {
        ApplyColorSpace();
    }

    public override Format BackBufferFormat => HdrBackBufferFormat;

    public override ColorSpaceType ColorSpace => HdrColorSpace;

    public override Dx12ShaderSet Shaders => Dx12ShaderCatalog.Hdr;

    private static IDXGISwapChain3 CreateHdrNativeSwapChain(
        IDXGIFactory2 factory,
        ID3D12CommandQueue commandQueue,
        nint hwnd,
        int width,
        int height,
        int frameCount,
        SwapChainFlags flags)
    {
        var swapChain = CreateNativeSwapChain(factory, commandQueue, hwnd, width, height, frameCount, HdrBackBufferFormat, flags);
        if (SupportsHdrColorSpace(swapChain))
            return swapChain;

        swapChain.Dispose();
        throw new NotSupportedException("The swapchain cannot present RGB ST.2084 Rec.2020.");
    }

    private static bool SupportsHdrColorSpace(IDXGISwapChain3 swapChain)
    {
        var support = swapChain.CheckColorSpaceSupport(HdrColorSpace);
        return (support & SwapChainColorSpaceSupportFlags.Present) != 0;
    }
}
