using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Sacred.Engine;

public sealed partial class FrameClock
{
    private long _last = Stopwatch.GetTimestamp();
    private long _lastPresent = Stopwatch.GetTimestamp();
    private readonly long _fallbackFrameTicks = Stopwatch.Frequency / 60;
    private readonly TimeSpan _internalFramePeriod = TimeSpan.FromSeconds(1.0 / 60.0);

    public float Tick()
    {
        var now = Stopwatch.GetTimestamp();
        var dt = (float)((now - _last) / (double)Stopwatch.Frequency);
        _last = now;
        return Math.Clamp(dt, 0.0f, 0.1f);
    }

    public void WaitForVSync()
    {
        if (DwmFlush() == 0)
        {
            _lastPresent = Stopwatch.GetTimestamp();
            return;
        }

        var target = _lastPresent + _fallbackFrameTicks;
        while (Stopwatch.GetTimestamp() < target)
            Thread.Sleep(1);

        _lastPresent = Stopwatch.GetTimestamp();
    }

    public async Task WaitForFrameStartAsync(FramePacingMode mode, CancellationToken cancellationToken)
    {
        if (mode == FramePacingMode.VSync)
            await WaitForVSyncAsync(cancellationToken);
    }

    public async Task WaitForFrameEndAsync(FramePacingMode mode, TimeSpan iterationTime, CancellationToken cancellationToken)
    {
        if (mode == FramePacingMode.InternalFrameLimiter)
            await WaitForInternalFrameLimitAsync(iterationTime, cancellationToken);
    }

    private async Task WaitForVSyncAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var result = await Task.Run(static () => DwmFlush(), cancellationToken);
        if (result == 0)
        {
            _lastPresent = Stopwatch.GetTimestamp();
            return;
        }

        await WaitForFallbackFramePeriodAsync(TimeSpan.Zero, cancellationToken);
    }

    private async Task WaitForInternalFrameLimitAsync(TimeSpan iterationTime, CancellationToken cancellationToken)
    {
        await WaitForFallbackFramePeriodAsync(iterationTime, cancellationToken);
    }

    private async Task WaitForFallbackFramePeriodAsync(TimeSpan elapsedInPeriod, CancellationToken cancellationToken)
    {
        var delay = _internalFramePeriod - elapsedInPeriod;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken);

        _lastPresent = Stopwatch.GetTimestamp();
    }

    [LibraryImport("dwmapi")]
    private static partial int DwmFlush();
}
