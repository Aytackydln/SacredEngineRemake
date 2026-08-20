using System;

namespace Sacred.Engine.Latency;

public sealed class LowLatencySystem : IDisposable
{
    private readonly NvidiaReflexNativeBridge? _nvidiaReflex;
    private readonly AmdAntiLag2Backend _amdAntiLag2 = new();
    private LatencyBackendKind _activeBackend = LatencyBackendKind.Generic;
    private LowLatencyMode _mode = LowLatencyMode.On;
    private uint _maxFps;
    private bool _disposed;

    private LowLatencySystem(NvidiaReflexNativeBridge? nvidiaReflex)
    {
        _nvidiaReflex = nvidiaReflex;
    }

    public LowLatencyMode Mode => _mode;

    public string ActiveBackendName => _activeBackend switch
    {
        LatencyBackendKind.NvidiaReflex => "NVIDIA Reflex",
        LatencyBackendKind.AmdAntiLag2 => "AMD Anti-Lag 2",
        _ => _nvidiaReflex?.IsPclAvailable == true ? "Generic + PCL markers" : "Generic"
    };

    public static LowLatencySystem CreateDefault()
    {
        _ = NvidiaReflexNativeBridge.TryCreate(out var nvidiaReflex);
        return new LowLatencySystem(nvidiaReflex);
    }

    public void AttachD3D12(nint device, nint commandQueue)
    {
        ThrowIfDisposed();

        _nvidiaReflex?.AttachD3D12(device, commandQueue);
        _ = _amdAntiLag2.TryInitialize(device);
        SelectActiveBackend();
        ApplyMode();
    }

    public void SetMode(LowLatencyMode mode, uint maxFps)
    {
        ThrowIfDisposed();

        if (_mode == mode && _maxFps == maxFps)
            return;

        _mode = mode;
        _maxFps = maxFps;
        ApplyMode();
    }

    public LowLatencyMode CycleMode()
    {
        var next = _mode switch
        {
            LowLatencyMode.Off => LowLatencyMode.On,
            LowLatencyMode.On => LowLatencyMode.OnPlusBoost,
            _ => LowLatencyMode.Off
        };

        SetMode(next, _maxFps);
        return next;
    }

    public void BeginFrame(ulong frameId)
    {
        ThrowIfDisposed();
        _nvidiaReflex?.BeginFrame(frameId);
    }

    public void SleepBeforeInput(ulong frameId)
    {
        ThrowIfDisposed();

        _nvidiaReflex?.Sleep(frameId);

        if (_activeBackend == LatencyBackendKind.AmdAntiLag2)
        {
            _amdAntiLag2.SleepBeforeInput(_mode != LowLatencyMode.Off, _maxFps);
            if (!_amdAntiLag2.IsAvailable)
                SelectActiveBackend();
        }
    }

    internal void Mark(LatencyMarker marker, ulong frameId)
    {
        ThrowIfDisposed();
        _nvidiaReflex?.Mark(marker, frameId);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _amdAntiLag2.Dispose();
        _nvidiaReflex?.Dispose();
    }

    private void SelectActiveBackend()
    {
        _activeBackend = _nvidiaReflex?.IsReflexAvailable == true
            ? LatencyBackendKind.NvidiaReflex
            : _amdAntiLag2.IsAvailable
                ? LatencyBackendKind.AmdAntiLag2
                : LatencyBackendKind.Generic;
    }

    private void ApplyMode()
    {
        _nvidiaReflex?.SetMode(_mode, _maxFps);

        if (_activeBackend == LatencyBackendKind.AmdAntiLag2 && _mode == LowLatencyMode.Off)
            _amdAntiLag2.SleepBeforeInput(false, 0);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
