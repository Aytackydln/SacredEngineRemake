using System;
using System.Runtime.InteropServices;
using Sacred.Engine.Extern;

namespace Sacred.Engine.Latency;

internal sealed unsafe class AmdAntiLag2Backend : IDisposable
{
    private const int SOk = 0;
    private const int SFalse = 1;
    private const uint AntiLagModeOn = 1;
    private const uint AntiLagModeOff = 2;
    private const uint ApiDataV2SignalFrameGenFrameType = 1u << 2;
    private const uint ApiDataV2InterpolatedFrame = 1u << 3;
    private const uint ApiDataV2SignalEndOfFrame = 1u << 5;
    private static readonly Guid AntiLagApiId = new("44085fbe-e839-40c5-bf38-0ebc5ab4d0a6");

    private nint _antiLagApi;
    private bool _enabled;
    private uint _maxFps;

    public bool IsAvailable => _antiLagApi != 0;

    public bool TryInitialize(nint d3d12Device)
    {
        if (_antiLagApi != 0)
            return true;

        if (d3d12Device == 0)
            return false;

        var amdDx12Module = Kernel32.GetModuleHandleA("amdxc64.dll");
        if (amdDx12Module == 0)
            return false;

        var createInterfaceAddress = Kernel32.GetProcAddress(amdDx12Module, "AmdExtD3DCreateInterface");
        if (createInterfaceAddress == 0)
            return false;

        var createInterface = (delegate* unmanaged[Cdecl]<nint, Guid*, void**, int>)createInterfaceAddress;
        var antiLagApiId = AntiLagApiId;
        void* antiLagApi = null;
        var hr = createInterface(d3d12Device, &antiLagApiId, &antiLagApi);
        if (hr != SOk || antiLagApi == null)
            return false;

        _antiLagApi = (nint)antiLagApi;
        if (SetAntiLagState(false, 0) == SOk)
            return true;

        Dispose();
        return false;
    }

    public void SleepBeforeInput(bool enabled, uint maxFps)
    {
        if (_antiLagApi == 0)
            return;

        if (_enabled != enabled || _maxFps != maxFps)
        {
            if (SetAntiLagState(enabled, maxFps) == SOk)
            {
                _enabled = enabled;
                _maxFps = maxFps;
            }
        }

        var hr = UpdateAntiLagState(null);
        if (hr != SOk && hr != SFalse)
            Dispose();
    }

    public void MarkEndOfFrameRendering()
    {
        if (_antiLagApi == 0)
            return;

        _ = SetAntiLagFrameGenState(ApiDataV2SignalEndOfFrame);
    }

    public void SetFrameGenFrameType(bool interpolated)
    {
        if (_antiLagApi == 0)
            return;

        var flags = ApiDataV2SignalFrameGenFrameType;
        if (interpolated)
            flags |= ApiDataV2InterpolatedFrame;

        _ = SetAntiLagFrameGenState(flags);
    }

    public void Dispose()
    {
        if (_antiLagApi == 0)
            return;

        var vtable = *(nint**)_antiLagApi;
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)vtable[2];
        _ = release(_antiLagApi);
        _antiLagApi = 0;
        _enabled = false;
        _maxFps = 0;
    }

    private int SetAntiLagState(bool enabled, uint maxFps)
    {
        var data = new ApiDataV1
        {
            Size = (uint)sizeof(ApiDataV1),
            Version = 1,
            Mode = enabled ? AntiLagModeOn : AntiLagModeOff,
            ControlString = 0,
            ControlStringLength = 0,
            MaxFps = maxFps
        };

        return UpdateAntiLagState(&data);
    }

    private int SetAntiLagFrameGenState(uint flags)
    {
        var data = new ApiDataV2
        {
            Size = (uint)sizeof(ApiDataV2),
            Version = 2,
            Flags = flags,
            FrameIndex = 0
        };

        return UpdateAntiLagState(&data);
    }

    private int UpdateAntiLagState(void* data)
    {
        var vtable = *(nint**)_antiLagApi;
        var updateAntiLagState = (delegate* unmanaged[Stdcall]<nint, void*, int>)vtable[3];
        return updateAntiLagState(_antiLagApi, data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ApiDataV1
    {
        public uint Size;
        public uint Version;
        public uint Mode;
        public nint ControlString;
        public uint ControlStringLength;
        public uint MaxFps;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ApiDataV2
    {
        public uint Size;
        public uint Version;
        public uint Flags;
        public uint Padding;
        public ulong FrameIndex;
        public fixed ulong Reserved[19];
    }
}
