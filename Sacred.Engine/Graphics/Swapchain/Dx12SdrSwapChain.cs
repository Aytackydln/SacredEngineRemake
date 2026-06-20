using Sacred.Shaders;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Swapchain;

internal sealed class Dx12SdrSwapChain : Dx12SwapChain
{
    public const Format SdrBackBufferFormat = Format.B8G8R8A8_UNorm;
    public const ColorSpaceType SdrColorSpace = ColorSpaceType.RgbFullG22NoneP709;

    public Dx12SdrSwapChain(
        IDXGIFactory2 factory,
        ID3D12CommandQueue commandQueue,
        nint hwnd,
        int width,
        int height,
        int frameCount,
        SwapChainFlags flags)
        : base(CreateNativeSwapChain(factory, commandQueue, hwnd, width, height, frameCount, SdrBackBufferFormat, flags))
    {
        ApplyColorSpace();
    }

    public override Format BackBufferFormat => SdrBackBufferFormat;

    public override ColorSpaceType ColorSpace => SdrColorSpace;

    public override Dx12ShaderSet Shaders => Dx12ShaderCatalog.Sdr;

    public override Dx12DisplayProfile DisplayProfile => Dx12DisplayProfile.Sdr;
}
