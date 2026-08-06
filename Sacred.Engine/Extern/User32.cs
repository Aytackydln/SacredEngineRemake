using System.Runtime.InteropServices;

namespace Sacred.Engine.Extern;

internal static partial class User32
{
    private const string LibraryName = "user32";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Wndclass
    {
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WndclassW
    {
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hwnd, out Rect rect);

    [LibraryImport(LibraryName, EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPtr(nint hwnd, int index, nint newLong);

    [LibraryImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(
        nint hwnd,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    internal static unsafe ushort RegisterClass(ref Wndclass lpWndClass)
    {
        fixed (char* menuName = lpWndClass.lpszMenuName)
        fixed (char* className = lpWndClass.lpszClassName)
        {
            var native = new WndclassW
            {
                style = lpWndClass.style,
                lpfnWndProc = lpWndClass.lpfnWndProc,
                cbClsExtra = lpWndClass.cbClsExtra,
                cbWndExtra = lpWndClass.cbWndExtra,
                hInstance = lpWndClass.hInstance,
                hIcon = lpWndClass.hIcon,
                hCursor = lpWndClass.hCursor,
                hbrBackground = lpWndClass.hbrBackground,
                lpszMenuName = menuName,
                lpszClassName = className,
            };

            return RegisterClassW(ref native);
        }
    }

    [LibraryImport(LibraryName, EntryPoint = "RegisterClassW", SetLastError = true)]
    private static unsafe partial ushort RegisterClassW(ref WndclassW lpWndClass);

    [LibraryImport(LibraryName, EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint CreateWindowEx(
        uint exStyle, string className, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    [LibraryImport(LibraryName, EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowText(nint hwnd, string title);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint hwnd, int nCmdShow);

    [LibraryImport(LibraryName)]
    internal static partial nint SetFocus(nint hwnd);

    [LibraryImport(LibraryName, EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessage(out Msg lpMsg, nint hWnd, uint min, uint max, uint remove);

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static unsafe partial uint MsgWaitForMultipleObjectsEx(
        uint nCount,
        nint* pHandles,
        uint dwMilliseconds,
        uint dwWakeMask,
        uint dwFlags);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(ref Msg lpMsg);

    [LibraryImport(LibraryName, EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(ref Msg lpMsg);

    [LibraryImport(LibraryName, EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport(LibraryName)]
    internal static partial void PostQuitMessage(int code);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hwnd);

    [LibraryImport(LibraryName)]
    internal static partial nint GetDC(nint hwnd);

    [LibraryImport(LibraryName)]
    internal static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(nint hwnd, out Rect rect);

    [LibraryImport(LibraryName)]
    internal static partial int FillRect(nint hdc, ref Rect rect, nint hBrush);

    [LibraryImport(LibraryName)]
    internal static partial int GetSystemMetrics(int index);

    [LibraryImport(LibraryName, EntryPoint = "LoadCursorW")]
    internal static partial nint LoadCursor(nint instance, nint cursorName);

    [LibraryImport(LibraryName)]
    internal static partial nint SetCursor(nint cursor);
}
