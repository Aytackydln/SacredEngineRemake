using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Sacred.Engine.Extern;

namespace Sacred.Engine.Platform;

public sealed partial class Win32Window
{
    /// <summary>Changes the outer window dimensions without activation or movement.</summary>
    public void SetWindowedSize(int width, int height)
    {
        if (IsBorderlessFullscreen || User32.IsZoomed(Hwnd))
            throw new InvalidOperationException("Window sizing requires a restored windowed window.");
        if (width is < 320 or > 16384 || height is < 320 or > 16384)
            throw new ArgumentOutOfRangeException(nameof(width), "Window dimensions must be between 320 and 16384.");
        const uint noMove = 0x0002;
        const uint noActivate = 0x0010;
        if (!User32.SetWindowPos(Hwnd, 0, 0, 0, width, height,
                noMove | noActivate | SetWindowPosNoZOrder | SetWindowPosNoOwnerZOrder))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not resize the window.");
        EngineLog.WriteLine($"Debug input: window size {width}x{height}, client {ClientWidth}x{ClientHeight}.");
    }
}
