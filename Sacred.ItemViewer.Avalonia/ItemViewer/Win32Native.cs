using System;
using System.Runtime.InteropServices;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

internal static partial class Win32Native
{
    private const string User32 = "user32";
    private const string Kernel32 = "kernel32";
    private const string Gdi32 = "gdi32";

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam);

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
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    internal static unsafe ushort RegisterClass(ref Wndclass wndclass)
    {
        fixed (char* menuName = wndclass.lpszMenuName)
        fixed (char* className = wndclass.lpszClassName)
        {
            var native = new WndclassW
            {
                style = wndclass.style,
                lpfnWndProc = wndclass.lpfnWndProc,
                cbClsExtra = wndclass.cbClsExtra,
                cbWndExtra = wndclass.cbWndExtra,
                hInstance = wndclass.hInstance,
                hIcon = wndclass.hIcon,
                hCursor = wndclass.hCursor,
                hbrBackground = wndclass.hbrBackground,
                lpszMenuName = menuName,
                lpszClassName = className,
            };

            return RegisterClassW(ref native);
        }
    }

    [LibraryImport(User32, EntryPoint = "RegisterClassW", SetLastError = true)]
    private static unsafe partial ushort RegisterClassW(ref WndclassW wndclass);

    [LibraryImport(User32, EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint CreateWindowEx(
        uint exStyle,
        string className,
        string title,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hwnd);

    [LibraryImport(User32, EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport(User32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(nint hwnd, out Rect rect);

    [LibraryImport(User32)]
    internal static partial short GetKeyState(int virtualKey);

    [LibraryImport(User32)]
    internal static partial nint SetFocus(nint hwnd);

    [LibraryImport(Kernel32, EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint GetModuleHandle(string? moduleName);

    [LibraryImport(Kernel32, EntryPoint = "CreateEventW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint CreateEvent(nint attributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset, [MarshalAs(UnmanagedType.Bool)] bool initialState, string? name);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [LibraryImport(Gdi32)]
    internal static partial nint GetStockObject(int objectId);
}
