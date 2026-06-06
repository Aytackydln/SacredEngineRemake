using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SacredItemSimulator.Avalonia.ItemViewer;

internal sealed class EmbeddedRenderWindow : IDisposable
{
    private const int ErrorClassAlreadyExists = 1410;
    private const int ColorBlack = 4;
    private const uint ClassStyleOwnDc = 0x0020;
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipChildren = 0x02000000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WmEraseBackground = 0x0014;
    private const uint WmKeyDown = 0x0100;
    private const uint WmLeftButtonDown = 0x0201;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmDestroy = 0x0002;

    private readonly Win32Native.WndProc _wndProc;
    private readonly Action<int> _mouseWheel;
    private readonly string _className;
    private bool _disposed;

    public nint Hwnd { get; }

    public EmbeddedRenderWindow(
        nint parentHwnd,
        Action<int> mouseWheel
    )
    {
        if (parentHwnd == 0)
            throw new ArgumentException("A valid parent HWND is required.", nameof(parentHwnd));

        _mouseWheel = mouseWheel;
        _wndProc = WindowProc;
        _className = "SacredItemViewerDx12Host" + Environment.ProcessId;

        var wc = new Win32Native.Wndclass
        {
            style = ClassStyleOwnDc,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = Win32Native.GetModuleHandle(null),
            hbrBackground = Win32Native.GetStockObject(ColorBlack),
            lpszClassName = _className,
        };

        var atom = Win32Native.RegisterClass(ref wc);
        if (atom == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != ErrorClassAlreadyExists)
                throw new Win32Exception(error, $"RegisterClassW failed for '{_className}'.");
        }

        Hwnd = Win32Native.CreateWindowEx(
            0,
            _className,
            "Sacred Item DX12 Viewer",
            WsChild | WsVisible | WsClipChildren | WsClipSiblings,
            0,
            0,
            1,
            1,
            parentHwnd,
            0,
            wc.hInstance,
            0);

        if (Hwnd == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateWindowExW failed for embedded DX12 viewer.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (Hwnd != 0)
            Win32Native.DestroyWindow(Hwnd);
    }

    private nint WindowProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case WmEraseBackground:
                return 1;
            case WmLeftButtonDown:
                Win32Native.SetFocus(hwnd);
                break;
            case WmKeyDown:
                break;
            case WmMouseWheel:
                _mouseWheel(GetSignedHighWord((nint)wParam));
                return 0;
            case WmDestroy:
                return 0;
        }

        return Win32Native.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static int GetSignedHighWord(nint value) => unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));
}
