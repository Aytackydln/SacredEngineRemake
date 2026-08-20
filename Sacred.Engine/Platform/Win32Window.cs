using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Sacred.Engine.Extern;

namespace Sacred.Engine.Platform;

public sealed class Win32Window : IDisposable
{
    private const int BlackBrush = 4;
    private const int ScreenWidthMetric = 0;
    private const int ScreenHeightMetric = 1;
    private const int VerticalRefreshCapsIndex = 116;
    private const int XButton1 = 1;
    private const int XButton2 = 2;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowStyleOverlappedWindow = 0x00CF0000;
    private const uint WindowStylePopup = 0x80000000;
    private const int WindowStyleIndex = -16;
    private const uint SetWindowPosNoZOrder = 0x0004;
    private const uint SetWindowPosFrameChanged = 0x0020;
    private const uint SetWindowPosNoOwnerZOrder = 0x0200;
    private const uint DefaultDisplayRefreshRate = 60;
    private const int ArrowCursorId = 32512;
    private const int HandCursorId = 32649;
    private const int HitTestClient = 1;

    private readonly WndProc _wndProc;
    private readonly string _className;
    private readonly nint _arrowCursor;
    private readonly nint _handCursor;
    private nint _requestedCursor;
    private int _windowedX = 100;
    private int _windowedY = 100;
    private int _windowedWidth;
    private int _windowedHeight;
    private bool _quitRequested;
    private bool _disposed;

    public nint Hwnd { get; }
    public int Width { get; }
    public int Height { get; }
    public bool IsBorderlessFullscreen { get; private set; }
    public int WindowedWidth
    {
        get
        {
            RememberCurrentWindowedBounds();
            return _windowedWidth;
        }
    }

    public int WindowedHeight
    {
        get
        {
            RememberCurrentWindowedBounds();
            return _windowedHeight;
        }
    }
    public uint DisplayRefreshRateHz { get; }
    public int ClientWidth
    {
        get
        {
            User32.GetClientRect(Hwnd, out var rect);
            return Math.Max(1, rect.Right - rect.Left);
        }
    }

    public int ClientHeight
    {
        get
        {
            User32.GetClientRect(Hwnd, out var rect);
            return Math.Max(1, rect.Bottom - rect.Top);
        }
    }

    public InputState Input { get; } = new();

    public Win32Window(string title, int width, int height, bool borderlessFullscreen = false)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Window dimensions must be positive.");

        _className = "SacredRemakeWindow" + Environment.ProcessId;
        _wndProc = WindowProc;
        _arrowCursor = User32.LoadCursor(0, ArrowCursorId);
        _handCursor = User32.LoadCursor(0, HandCursorId);
        if (_arrowCursor == 0 || _handCursor == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not load system cursors.");
        _requestedCursor = _arrowCursor;

        var wc = new User32.Wndclass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = Kernel32.GetModuleHandle(null),
            hbrBackground = Gdi32.GetStockObject(BlackBrush),
            hCursor = _arrowCursor,
            lpszClassName = _className,
        };
        var classAtom = User32.RegisterClass(ref wc);
        if (classAtom == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != 1410) // ERROR_CLASS_ALREADY_EXISTS
            {
                throw new Win32Exception(error, $"RegisterClassW failed for '{_className}'.");
            }
        }

        IsBorderlessFullscreen = borderlessFullscreen;
        _windowedWidth = width;
        _windowedHeight = height;
        var windowStyle = WindowStyleVisible | (borderlessFullscreen ? WindowStylePopup : WindowStyleOverlappedWindow);
        var windowX = borderlessFullscreen ? 0 : 100;
        var windowY = borderlessFullscreen ? 0 : 100;
        Width = borderlessFullscreen ? Math.Max(1, User32.GetSystemMetrics(ScreenWidthMetric)) : width;
        Height = borderlessFullscreen ? Math.Max(1, User32.GetSystemMetrics(ScreenHeightMetric)) : height;

        Hwnd = User32.CreateWindowEx(0, _className, title, windowStyle, windowX, windowY, Width, Height, 0, 0, wc.hInstance, 0);
        if (Hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"CreateWindowExW failed for '{_className}'.");
        }

        DisplayRefreshRateHz = QueryDisplayRefreshRate();
        User32.ShowWindow(Hwnd, 5);
        SetTitle("SacredEngineRemake");
    }

    public void SetTitle(string title) => User32.SetWindowText(Hwnd, title);

    /// <summary>Switches between a primary-display-sized borderless window and the saved windowed bounds.</summary>
    public bool ToggleBorderlessFullscreen()
    {
        SetBorderlessFullscreen(!IsBorderlessFullscreen);
        return IsBorderlessFullscreen;
    }

    public void SetBorderlessFullscreen(bool enabled)
    {
        if (IsBorderlessFullscreen == enabled)
            return;

        if (enabled)
            RememberWindowedBounds();

        var style = WindowStyleVisible | (enabled ? WindowStylePopup : WindowStyleOverlappedWindow);
        User32.SetWindowLongPtr(Hwnd, WindowStyleIndex, unchecked((nint)style));

        var x = enabled ? 0 : _windowedX;
        var y = enabled ? 0 : _windowedY;
        var width = enabled ? Math.Max(1, User32.GetSystemMetrics(ScreenWidthMetric)) : _windowedWidth;
        var height = enabled ? Math.Max(1, User32.GetSystemMetrics(ScreenHeightMetric)) : _windowedHeight;
        if (!User32.SetWindowPos(
                Hwnd,
                0,
                x,
                y,
                width,
                height,
                SetWindowPosNoZOrder | SetWindowPosFrameChanged | SetWindowPosNoOwnerZOrder))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not change window mode.");
        }

        IsBorderlessFullscreen = enabled;
        EngineLog.WriteLine($"Window mode: {(enabled ? "borderless fullscreen" : $"windowed {_windowedWidth}x{_windowedHeight}")}");
    }

    public void RequestFocus() => User32.SetFocus(Hwnd);

    public void SetHandCursor(bool enabled)
    {
        var cursor = enabled ? _handCursor : _arrowCursor;
        if (_requestedCursor == cursor)
            return;

        _requestedCursor = cursor;
        User32.SetCursor(cursor);
    }

    public bool ProcessMessages()
    {
        if (_quitRequested) return false;

        while (User32.PeekMessage(out var msg, 0, 0, 0, 1))
        {
            if (msg.message == Win32WindowEventCodes.WmQuit)
            {
                _quitRequested = true;
                return false;
            }

            User32.TranslateMessage(ref msg);
            User32.DispatchMessage(ref msg);
        }
        return true;
    }

    private nint WindowProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case 0x0020 when GetUnsignedLowWord(lParam) == HitTestClient: // WM_SETCURSOR
                User32.SetCursor(_requestedCursor);
                return 1;
            case 0x0002: // WM_DESTROY
                _quitRequested = true;
                User32.PostQuitMessage(0);
                return 0;
            case 0x0014: // WM_ERASEBKGND
                User32.GetClientRect(hwnd, out var rect);
                User32.FillRect((nint)wParam, ref rect, Gdi32.GetStockObject(BlackBrush));
                return 1;
            case 0x0100: // WM_KEYDOWN
            case 0x0104: // WM_SYSKEYDOWN (includes F10)
                Input.Set((VirtualKey)wParam, true);
                return 0;
            case 0x0101: // WM_KEYUP
            case 0x0105: // WM_SYSKEYUP
                Input.Set((VirtualKey)wParam, false);
                return 0;
            case 0x0200: // WM_MOUSEMOVE
                Input.SetMousePosition(GetMouseX(lParam), GetMouseY(lParam));
                return 0;
            case 0x0201: // WM_LBUTTONDOWN
                Input.SetLeftMouseButton(true, GetMouseX(lParam), GetMouseY(lParam));
                return 0;
            case 0x0202: // WM_LBUTTONUP
                Input.SetLeftMouseButton(false, GetMouseX(lParam), GetMouseY(lParam));
                return 0;
            case 0x0204: // WM_RBUTTONDOWN
                Input.SetRightMouseButton(true, GetMouseX(lParam), GetMouseY(lParam));
                return 0;
            case 0x0205: // WM_RBUTTONUP
                Input.SetRightMouseButton(false, GetMouseX(lParam), GetMouseY(lParam));
                return 0;
            case 0x0207: // WM_MBUTTONDOWN
                Input.SetMiddleMouseButton(true, GetMouseX(lParam), GetMouseY(lParam));
                return 0;
            case 0x0208: // WM_MBUTTONUP
                Input.SetMiddleMouseButton(false, GetMouseX(lParam), GetMouseY(lParam));
                return 0;
            case 0x020A: // WM_MOUSEWHEEL
                Input.AddMouseWheelDelta(GetSignedHighWord((nint)wParam));
                return 0;
            case 0x020B: // WM_XBUTTONDOWN
                if (GetUnsignedHighWord((nint)wParam) is XButton1 or XButton2)
                    Input.PressXButtonCycle();
                return 1;
        }
        return User32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static int GetMouseX(nint lParam) => unchecked((short)(lParam.ToInt64() & 0xFFFF));

    private static int GetMouseY(nint lParam) => unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

    private static int GetSignedHighWord(nint value) => unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));

    private static int GetUnsignedHighWord(nint value) => unchecked((ushort)((value.ToInt64() >> 16) & 0xFFFF));

    private static int GetUnsignedLowWord(nint value) => unchecked((ushort)(value.ToInt64() & 0xFFFF));

    private uint QueryDisplayRefreshRate()
    {
        var hdc = User32.GetDC(Hwnd);
        if (hdc == 0)
            return DefaultDisplayRefreshRate;

        try
        {
            var refreshRate = Gdi32.GetDeviceCaps(hdc, VerticalRefreshCapsIndex);
            return refreshRate > 1 ? (uint)refreshRate : DefaultDisplayRefreshRate;
        }
        finally
        {
            User32.ReleaseDC(Hwnd, hdc);
        }
    }

    private void RememberWindowedBounds()
    {
        if (!User32.GetWindowRect(Hwnd, out var rect))
            return;

        _windowedX = rect.Left;
        _windowedY = rect.Top;
        _windowedWidth = Math.Max(1, rect.Right - rect.Left);
        _windowedHeight = Math.Max(1, rect.Bottom - rect.Top);
    }

    private void RememberCurrentWindowedBounds()
    {
        if (!IsBorderlessFullscreen)
            RememberWindowedBounds();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Hwnd != 0) User32.DestroyWindow(Hwnd);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam);
    
}
