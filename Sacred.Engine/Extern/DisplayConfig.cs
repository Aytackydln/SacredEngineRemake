using System.Runtime.InteropServices;

namespace Sacred.Engine.Extern;

/// <summary>Read-only display topology and SDR-white queries available through user32/Wine.</summary>
internal static unsafe partial class DisplayConfig
{
    internal const uint ActivePaths = 2;
    internal const int InsufficientBuffer = 122;

    [StructLayout(LayoutKind.Sequential)]
    internal struct AdapterId
    {
        internal uint Low;
        internal int High;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    internal struct PathInfo
    {
        [FieldOffset(0)] internal AdapterId SourceAdapter;
        [FieldOffset(8)] internal uint SourceId;
        [FieldOffset(20)] internal AdapterId TargetAdapter;
        [FieldOffset(28)] internal uint TargetId;
    }

    // DISPLAYCONFIG_MODE_INFO contains a 48-byte union following its 16-byte header.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct ModeInfo
    {
        [FieldOffset(0)] internal uint InfoType;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInfoHeader
    {
        internal uint Type;
        internal uint Size;
        internal AdapterId Adapter;
        internal uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SourceDeviceName
    {
        internal DeviceInfoHeader Header;
        internal fixed char Name[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SdrWhiteLevel
    {
        internal DeviceInfoHeader Header;
        internal uint Level;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        internal uint Size;
        internal User32.Rect Monitor;
        internal User32.Rect Work;
        internal uint Flags;
        internal fixed char DeviceName[32];
    }

    [LibraryImport("user32", EntryPoint = "MonitorFromWindow")]
    internal static partial nint MonitorFromWindow(nint window, uint flags);

    [LibraryImport("user32", EntryPoint = "GetMonitorInfoW")]
    internal static partial int GetMonitorInfo(nint monitor, MonitorInfo* info);

    [LibraryImport("user32", EntryPoint = "GetDisplayConfigBufferSizes")]
    internal static partial int GetBufferSizes(uint flags, out uint paths, out uint modes);

    [LibraryImport("user32", EntryPoint = "QueryDisplayConfig")]
    internal static partial int Query(uint flags, ref uint paths, PathInfo* pathInfo,
        ref uint modes, ModeInfo* modeInfo, nint topology);

    [LibraryImport("user32", EntryPoint = "DisplayConfigGetDeviceInfo")]
    internal static partial int GetDeviceInfo(DeviceInfoHeader* header);
}
