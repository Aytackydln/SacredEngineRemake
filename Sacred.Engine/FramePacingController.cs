using System;
using System.Threading;
using Sacred.Engine.Graphics;
using Sacred.Engine.Latency;

namespace Sacred.Engine;

/// <summary>Owns presentation policy, the CPU limiter, and the latency backend's frame-rate contract.</summary>
internal sealed class FramePacingController : IDisposable
{
    private readonly Dx12Renderer _renderer;
    private readonly LowLatencySystem _latency;
    private readonly HighResolutionFrameClock _clock;
    private FramePacingMode _mode;
    private string _status;

    public FramePacingController(
        Dx12Renderer renderer,
        LowLatencySystem latency,
        uint displayRefreshRateHz,
        FramePacingMode mode,
        LowLatencyMode lowLatencyMode)
    {
        _renderer = renderer;
        _latency = latency;
        _clock = new HighResolutionFrameClock(displayRefreshRateHz);
        _mode = mode;
        _status = FormatStatus();
        SetLowLatencyMode(lowLatencyMode);
    }

    public FramePacingMode Mode => _mode;

    public uint TargetFrameRate => _clock.TargetFrameRate;

    public bool VerticalSyncEnabled =>
        _mode == FramePacingMode.VSync ||
        (_mode == FramePacingMode.VariableRefreshRate && !_renderer.VariableRefreshRateSupported);

    public string Status => _status;

    public void WaitForFrameStart(CancellationToken cancellationToken)
    {
        _renderer.PrepareFrame(cancellationToken);
        _clock.WaitForFrameStart(UsesCpuLimiter, cancellationToken);
    }

    public float Tick() => _clock.Tick();

    public void BeginLatencyFrame(ulong frameId)
    {
        _latency.BeginFrame(frameId);
        _latency.SleepBeforeInput(frameId);
    }

    public void CycleMode() => SetMode(_mode switch
    {
        FramePacingMode.VariableRefreshRate => FramePacingMode.VSync,
        FramePacingMode.VSync => FramePacingMode.MonitorRefreshLimiter,
        _ => FramePacingMode.VariableRefreshRate
    });

    public void SetMode(FramePacingMode mode)
    {
        if (_mode == mode)
            return;

        _mode = mode;
        _status = FormatStatus();
        _clock.ResetPacing();
        ApplyLatencyMode();
    }

    public void SetLowLatencyMode(LowLatencyMode mode)
    {
        _latency.SetMode(mode, LatencyMaximumFrameRate);
    }

    public void Dispose() => _clock.Dispose();

    private bool UsesCpuLimiter =>
        _mode == FramePacingMode.MonitorRefreshLimiter ||
        (_mode == FramePacingMode.VariableRefreshRate && _renderer.VariableRefreshRateSupported);

    private uint LatencyMaximumFrameRate => VerticalSyncEnabled ? 0 : TargetFrameRate;

    private void ApplyLatencyMode() => _latency.SetMode(_latency.Mode, LatencyMaximumFrameRate);

    private string FormatStatus() => _mode switch
    {
        FramePacingMode.VariableRefreshRate => _renderer.VariableRefreshRateSupported
            ? $"VRR, {TargetFrameRate} FPS cap"
            : "VRR unavailable, VSync fallback",
        FramePacingMode.VSync => "VSync",
        FramePacingMode.MonitorRefreshLimiter => $"{TargetFrameRate} FPS limiter",
        _ => _mode.ToString()
    };
}
