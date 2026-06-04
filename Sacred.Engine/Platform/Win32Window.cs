using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Sacred.Engine.Extern;

namespace Sacred.Engine.Platform;

public sealed class Win32Window : IDisposable
{
    private const int BlackBrush = 4;

    private readonly WndProc _wndProc;
    private readonly string _className;
    private bool _disposed;

    public nint Hwnd { get; }
    public int Width { get; }
    public int Height { get; }
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

    public Win32Window(string title, int width, int height)
    {
        Width = width;
        Height = height;
        _className = "SacredRemakeWindow" + Environment.ProcessId;
        _wndProc = WindowProc;

        var wc = new User32.Wndclass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = Kernel32.GetModuleHandle(null),
            hbrBackground = Gdi32.GetStockObject(BlackBrush),
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

        Hwnd = User32.CreateWindowEx(0, _className, title, 0x10CF0000, 100, 100, width, height, 0, 0, wc.hInstance, 0);
        if (Hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"CreateWindowExW failed for '{_className}'.");
        }

        User32.ShowWindow(Hwnd, 5);
        SetTitle("SacredEngineRemake");
    }

    public void SetTitle(string title) => User32.SetWindowText(Hwnd, title);

    public bool ProcessMessages()
    {
        while (User32.PeekMessage(out var msg, 0, 0, 0, 1))
        {
            if (msg.message == 0x0012) return false; // WM_QUIT
            User32.TranslateMessage(ref msg);
            User32.DispatchMessage(ref msg);
        }
        return true;
    }

    private nint WindowProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case 0x0002: User32.PostQuitMessage(0); return 0; // WM_DESTROY
            case 0x0014: // WM_ERASEBKGND
                User32.GetClientRect(hwnd, out var rect);
                User32.FillRect((nint)wParam, ref rect, Gdi32.GetStockObject(BlackBrush));
                return 1;
            case 0x0100: Input.Set((VirtualKey)wParam, true); return 0; // WM_KEYDOWN
            case 0x0101: Input.Set((VirtualKey)wParam, false); return 0; // WM_KEYUP
            case 0x0200: Input.SetMousePosition(GetMouseX(lParam), GetMouseY(lParam)); return 0; // WM_MOUSEMOVE
            case 0x0201: Input.SetLeftMouseButton(true, GetMouseX(lParam), GetMouseY(lParam)); return 0; // WM_LBUTTONDOWN
            case 0x0202: Input.SetLeftMouseButton(false, GetMouseX(lParam), GetMouseY(lParam)); return 0; // WM_LBUTTONUP
        }
        return User32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static int GetMouseX(nint lParam) => unchecked((short)(lParam.ToInt64() & 0xFFFF));

    private static int GetMouseY(nint lParam) => unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Hwnd != 0) User32.DestroyWindow(Hwnd);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam);
    
}
