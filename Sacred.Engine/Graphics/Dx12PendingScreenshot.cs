using System;
using Sacred.Engine.Extern;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics;

/// <summary>Owns a submitted readback until a background worker observes its GPU fence.</summary>
internal sealed class Dx12PendingScreenshot(
    Dx12BackBufferCapture capture,
    ID3D12Fence fence,
    ulong fenceValue) : IDisposable
{
    public Dx12ScreenshotImage WaitAndRead()
    {
        if (fence.CompletedValue < fenceValue)
        {
            var fenceEvent = Kernel32.CreateEventA(IntPtr.Zero, false, false, null);
            if (fenceEvent == 0)
                throw new InvalidOperationException("Failed to create the screenshot fence event.");

            try
            {
                fence.SetEventOnCompletion(fenceValue, fenceEvent).CheckError();
                Kernel32.WaitForSingleObject(fenceEvent, Kernel32.Infinite);
            }
            finally
            {
                Kernel32.CloseHandle(fenceEvent);
            }
        }

        return capture.ReadPixels();
    }

    public void Dispose() => capture.Dispose();
}
