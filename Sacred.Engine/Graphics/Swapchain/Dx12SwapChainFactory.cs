using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Swapchain;

internal static class Dx12SwapChainFactory
{
    public static Dx12SwapChain Create(
        Dx12SwapChainMode mode,
        IDXGIFactory2 factory,
        ID3D12CommandQueue commandQueue,
        nint hwnd,
        int width,
        int height,
        int frameCount,
        SwapChainFlags flags)
    {
        if (mode == Dx12SwapChainMode.Hdr)
        {
            try
            {
                return new Dx12HdrSwapChain(factory, commandQueue, hwnd, width, height, frameCount, flags);
            }
            catch
            {
            }
        }

        return new Dx12SdrSwapChain(factory, commandQueue, hwnd, width, height, frameCount, flags);
    }
}