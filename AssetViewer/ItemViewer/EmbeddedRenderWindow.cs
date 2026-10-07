using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AssetViewer.ItemViewer;

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
    private const uint WmLeftButtonUp = 0x0202;
    private const uint WmMouseMove = 0x0200;
    private const uint WmCaptureChanged = 0x0215;
    private const uint WmCancelMode = 0x001F;
    private const nuint MkLeftButton = 0x0001;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmDestroy = 0x0002;
    private const uint WmNcDestroy = 0x0082;

    // RegisterClass retains only a function pointer. The shared class must keep its
    // callback alive beyond any individual viewport and dispatch by the actual HWND.
    private static readonly Win32Native.WndProc RegisteredWindowProc = DispatchWindowMessage;
    private static readonly ConcurrentDictionary<nint, EmbeddedRenderWindow> Windows = new();
    private readonly Action<int> _mouseWheel;
    private readonly Action<int>? _horizontalDrag;
    private bool _horizontalDragEnabled;
    private bool _dragging;
    private int _dragX;
    private readonly string _className;
    private bool _disposed;

    public nint Hwnd { get; }

    public EmbeddedRenderWindow(
        nint parentHwnd,
        Action<int> mouseWheel,
        Action<int>? horizontalDrag = null
    )
    {
        if (parentHwnd == 0)
            throw new ArgumentException("A valid parent HWND is required.", nameof(parentHwnd));

        _mouseWheel = mouseWheel;
        _horizontalDrag = horizontalDrag;
        _className = "SacredItemViewerDx12Host" + Environment.ProcessId;

        var wc = new Win32Native.Wndclass
        {
            style = ClassStyleOwnDc,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(RegisteredWindowProc),
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

        Windows[Hwnd] = this;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        EndDrag();

        // WM_NCDESTROY releases the owner even when Avalonia destroys the parent first.
        // Keep ownership on failure: Windows may still invoke the registered callback.
        if (!Win32Native.DestroyWindow(Hwnd))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "DestroyWindow failed for embedded DX12 viewer.");
    }

    public void SetHorizontalDragEnabled(bool enabled)
    {
        _horizontalDragEnabled = enabled;
        if (!enabled) EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        if (Win32Native.GetCapture() == Hwnd) Win32Native.ReleaseCapture();
        Console.WriteLine("[Assets] Preview drag ended.");
    }

    private static nint DispatchWindowMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (!Windows.TryGetValue(hwnd, out var window))
            return Win32Native.DefWindowProc(hwnd, msg, wParam, lParam);

        if (msg == WmNcDestroy)
        {
            window._disposed = true;
            Windows.TryRemove(hwnd, out _);
        }

        return window.WindowProc(hwnd, msg, wParam, lParam);
    }

    private nint WindowProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case WmEraseBackground:
                return 1;
            case WmLeftButtonDown:
                Win32Native.SetFocus(hwnd);
                if (_horizontalDragEnabled && _horizontalDrag is not null)
                {
                    _dragX = GetSignedLowWord(lParam);
                    _dragging = true;
                    Win32Native.SetCapture(hwnd);
                    Console.WriteLine("[Assets] Preview drag started.");
                    return 0;
                }
                break;
            case WmMouseMove when _dragging:
                if ((wParam & MkLeftButton) == 0) { EndDrag(); return 0; }
                var x = GetSignedLowWord(lParam);
                var delta = x - _dragX;
                _dragX = x;
                if (delta != 0) _horizontalDrag?.Invoke(delta);
                return 0;
            case WmLeftButtonUp when _dragging:
            case WmCaptureChanged:
            case WmCancelMode:
                EndDrag();
                break;
            case WmKeyDown:
                break;
            case WmMouseWheel:
                _mouseWheel(GetSignedHighWord((nint)wParam));
                return 0;
            case WmDestroy:
                EndDrag();
                return 0;
        }

        return Win32Native.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static int GetSignedHighWord(nint value) => unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));
    private static int GetSignedLowWord(nint value) => unchecked((short)(value.ToInt64() & 0xFFFF));
}
