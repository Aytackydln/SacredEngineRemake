using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sacred.Engine.Latency;

internal sealed unsafe class NvidiaReflexNativeBridge : IDisposable
{
    private const string LibraryFileName = "Sacred.NativeLatency.dll";
    private const uint SupportedAbiVersion = 1;
    private const uint ReflexCapability = 1u << 0;
    private const uint PclCapability = 1u << 1;

    private readonly nint _library;
    private readonly delegate* unmanaged[Cdecl]<void> _shutdown;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, int> _setD3D12Device;
    private readonly delegate* unmanaged[Cdecl]<int, uint, void> _setMode;
    private readonly delegate* unmanaged[Cdecl]<ulong, void> _beginFrame;
    private readonly delegate* unmanaged[Cdecl]<ulong, void> _sleep;
    private readonly delegate* unmanaged[Cdecl]<uint, ulong, void> _marker;
    private readonly delegate* unmanaged[Cdecl]<uint> _getCapabilities;
    private uint _capabilities;
    private bool _disposed;

    private NvidiaReflexNativeBridge(
        nint library,
        delegate* unmanaged[Cdecl]<void> shutdown,
        delegate* unmanaged[Cdecl]<nint, nint, int> setD3D12Device,
        delegate* unmanaged[Cdecl]<int, uint, void> setMode,
        delegate* unmanaged[Cdecl]<ulong, void> beginFrame,
        delegate* unmanaged[Cdecl]<ulong, void> sleep,
        delegate* unmanaged[Cdecl]<uint, ulong, void> marker,
        delegate* unmanaged[Cdecl]<uint> getCapabilities)
    {
        _library = library;
        _shutdown = shutdown;
        _setD3D12Device = setD3D12Device;
        _setMode = setMode;
        _beginFrame = beginFrame;
        _sleep = sleep;
        _marker = marker;
        _getCapabilities = getCapabilities;
        _capabilities = getCapabilities();
    }

    public bool IsReflexAvailable => (_capabilities & ReflexCapability) != 0;

    public bool IsPclAvailable => (_capabilities & PclCapability) != 0;

    public static bool TryCreate(out NvidiaReflexNativeBridge? bridge)
    {
        bridge = null;
        if (!TryLoadLibrary(out var library))
            return false;

        try
        {
            if (!TryGetExport(library, "sacred_latency_get_abi_version", out var getAbiVersionAddress) ||
                !TryGetExport(library, "sacred_latency_initialize", out var initializeAddress) ||
                !TryGetExport(library, "sacred_latency_shutdown", out var shutdownAddress) ||
                !TryGetExport(library, "sacred_latency_set_d3d12_device", out var setD3D12DeviceAddress) ||
                !TryGetExport(library, "sacred_latency_set_mode", out var setModeAddress) ||
                !TryGetExport(library, "sacred_latency_begin_frame", out var beginFrameAddress) ||
                !TryGetExport(library, "sacred_latency_sleep", out var sleepAddress) ||
                !TryGetExport(library, "sacred_latency_marker", out var markerAddress) ||
                !TryGetExport(library, "sacred_latency_get_capabilities", out var getCapabilitiesAddress))
            {
                NativeLibrary.Free(library);
                return false;
            }

            var getAbiVersion = (delegate* unmanaged[Cdecl]<uint>)getAbiVersionAddress;
            var initialize = (delegate* unmanaged[Cdecl]<int>)initializeAddress;
            var shutdown = (delegate* unmanaged[Cdecl]<void>)shutdownAddress;
            var setD3D12Device = (delegate* unmanaged[Cdecl]<nint, nint, int>)setD3D12DeviceAddress;
            var setMode = (delegate* unmanaged[Cdecl]<int, uint, void>)setModeAddress;
            var beginFrame = (delegate* unmanaged[Cdecl]<ulong, void>)beginFrameAddress;
            var sleep = (delegate* unmanaged[Cdecl]<ulong, void>)sleepAddress;
            var marker = (delegate* unmanaged[Cdecl]<uint, ulong, void>)markerAddress;
            var getCapabilities = (delegate* unmanaged[Cdecl]<uint>)getCapabilitiesAddress;

            if (getAbiVersion() != SupportedAbiVersion)
            {
                NativeLibrary.Free(library);
                return false;
            }

            if (initialize() != 0)
            {
                NativeLibrary.Free(library);
                return false;
            }

            bridge = new NvidiaReflexNativeBridge(
                library,
                shutdown,
                setD3D12Device,
                setMode,
                beginFrame,
                sleep,
                marker,
                getCapabilities);
            return true;
        }
        catch
        {
            NativeLibrary.Free(library);
            return false;
        }
    }

    public void AttachD3D12(nint device, nint commandQueue)
    {
        if (!_disposed)
        {
            _ = _setD3D12Device(device, commandQueue);
            _capabilities = _getCapabilities();
        }
    }

    public void SetMode(LowLatencyMode mode, uint maxFps)
    {
        if (!_disposed && IsReflexAvailable)
            _setMode((int)mode, maxFps);
    }

    public void BeginFrame(ulong frameId)
    {
        if (!_disposed && IsReflexAvailable)
            _beginFrame(frameId);
    }

    public void Sleep(ulong frameId)
    {
        if (!_disposed && IsReflexAvailable)
            _sleep(frameId);
    }

    public void Mark(LatencyMarker marker, ulong frameId)
    {
        if (!_disposed && IsPclAvailable)
            _marker((uint)marker, frameId);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _shutdown();
        NativeLibrary.Free(_library);
    }

    private static bool TryLoadLibrary(out nint library)
    {
        var localPath = Path.Combine(AppContext.BaseDirectory, LibraryFileName);
        if (File.Exists(localPath) && NativeLibrary.TryLoad(localPath, out library))
            return true;

        return NativeLibrary.TryLoad(LibraryFileName, out library);
    }

    private static bool TryGetExport(nint library, string name, out nint functionPointer)
    {
        if (NativeLibrary.TryGetExport(library, name, out var address) && address != 0)
        {
            functionPointer = address;
            return true;
        }

        functionPointer = 0;
        return false;
    }
}
