using System;
using System.Diagnostics;
using System.Threading;
using Sacred.Engine.Graphics;
using Sacred.Engine.Latency;

namespace Sacred.Engine;

/// <summary>Owns presentation policy, the CPU limiter, and the latency backend's frame-rate contract.</summary>
internal sealed class FramePacingController : IDisposable
{
    public const int MinimumManualFrameRate = 30;
    public const int MaximumManualFrameRate = 1000;

    private readonly Dx12Renderer _renderer;
    private readonly LowLatencySystem _latency;
    private readonly HighResolutionFrameClock _clock;
    private readonly uint _displayRefreshRateHz;
    private FramePacingMode _mode;
    private int _manualFrameRate;
    private string _status;

    public FramePacingController(
        Dx12Renderer renderer,
        LowLatencySystem latency,
        uint displayRefreshRateHz,
        FramePacingMode mode,
        int manualFrameRate,
        LowLatencyMode lowLatencyMode)
    {
        _renderer = renderer;
        _latency = latency;
        _displayRefreshRateHz = displayRefreshRateHz;
        _mode = mode;
        _manualFrameRate = NormalizeManualFrameRate(manualFrameRate);
        _clock = new HighResolutionFrameClock(TargetFrameRateForMode(mode));
        _status = FormatStatus();
        SetLowLatencyMode(lowLatencyMode);
    }

    public FramePacingMode Mode => _mode;

    public uint TargetFrameRate => _clock.TargetFrameRate;

    public int ManualFrameRate => _manualFrameRate;

    public bool VerticalSyncEnabled =>
        _mode == FramePacingMode.VSync ||
        (_mode == FramePacingMode.VariableRefreshRate && !_renderer.VariableRefreshRateSupported);

    public string Status => _status;

    public void WaitForFrameStart(CancellationToken cancellationToken)
    {
        // Do not consume DXGI's presentation slot while the CPU limiter is still waiting.
        // If the two waits drift out of phase, doing that can serialize them and halve throughput.
        var started = _renderer.FrameTiming.Enabled ? Stopwatch.GetTimestamp() : 0;
        _clock.WaitForFrameStart(UsesCpuLimiter, cancellationToken);
        LastCpuWaitMilliseconds = started == 0 ? 0 : Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _renderer.PrepareFrame(cancellationToken);
    }

    internal double LastCpuWaitMilliseconds { get; private set; }
    internal double LastLatencyWaitMilliseconds { get; private set; }

    public float Tick() => _clock.Tick();

    public void BeginLatencyFrame(ulong frameId)
    {
        var started = _renderer.FrameTiming.Enabled ? Stopwatch.GetTimestamp() : 0;
        _latency.BeginFrame(frameId);
        _latency.SleepBeforeInput(frameId);
        LastLatencyWaitMilliseconds = started == 0 ? 0 : Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    public void CycleMode() => SetMode(_mode switch
    {
        FramePacingMode.VariableRefreshRate => FramePacingMode.VSync,
        FramePacingMode.VSync => FramePacingMode.MonitorRefreshLimiter,
        FramePacingMode.MonitorRefreshLimiter => FramePacingMode.Manual,
        _ => FramePacingMode.VariableRefreshRate
    });

    public void SetMode(FramePacingMode mode)
    {
        if (_mode == mode)
            return;

        _mode = mode;
        _clock.SetTargetFrameRate(TargetFrameRateForMode(mode));
        _status = FormatStatus();
        ApplyLatencyMode();
    }

    public void SetManualFrameRate(int frameRate)
    {
        var normalizedFrameRate = NormalizeManualFrameRate(frameRate);
        if (_manualFrameRate == normalizedFrameRate)
            return;

        _manualFrameRate = normalizedFrameRate;
        if (_mode != FramePacingMode.Manual)
            return;

        _clock.SetTargetFrameRate((uint)_manualFrameRate);
        _status = FormatStatus();
        ApplyLatencyMode();
    }

    public void SetLowLatencyMode(LowLatencyMode mode)
    {
        // The CPU clock owns the FPS cap. A second driver limiter can drift against
        // both that clock and DXGI, making throughput depend on the startup phase.
        _latency.SetMode(mode, 0);
    }

    public void Dispose() => _clock.Dispose();

    private bool UsesCpuLimiter =>
        _mode is FramePacingMode.MonitorRefreshLimiter or FramePacingMode.Manual ||
        (_mode == FramePacingMode.VariableRefreshRate && _renderer.VariableRefreshRateSupported);

    private void ApplyLatencyMode() => _latency.SetMode(_latency.Mode, 0);

    private uint TargetFrameRateForMode(FramePacingMode mode) =>
        mode == FramePacingMode.Manual ? (uint)_manualFrameRate : _displayRefreshRateHz;

    private static int NormalizeManualFrameRate(int frameRate) =>
        Math.Clamp(frameRate, MinimumManualFrameRate, MaximumManualFrameRate);

    private string FormatStatus() => _mode switch
    {
        FramePacingMode.VariableRefreshRate => _renderer.VariableRefreshRateSupported
            ? $"VRR, {TargetFrameRate} FPS cap"
            : "VRR unavailable, VSync fallback",
        FramePacingMode.VSync => "VSync",
        FramePacingMode.MonitorRefreshLimiter => $"{TargetFrameRate} FPS limiter",
        FramePacingMode.Manual => $"Manual, {TargetFrameRate} FPS cap",
        _ => _mode.ToString()
    };
}
