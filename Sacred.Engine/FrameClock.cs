using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Sacred.Engine;

public sealed class FrameClock
{
    private long _last = Stopwatch.GetTimestamp();
    private readonly TimeSpan _targetFramePeriod;

    public FrameClock(uint targetFrameRate)
    {
        TargetFrameRate = Math.Clamp(targetFrameRate, 30u, 1000u);
        _targetFramePeriod = TimeSpan.FromSeconds(1.0 / TargetFrameRate);
    }

    public uint TargetFrameRate { get; }

    public float Tick()
    {
        var now = Stopwatch.GetTimestamp();
        var dt = (float)((now - _last) / (double)Stopwatch.Frequency);
        _last = now;
        return Math.Clamp(dt, 0.0f, 0.1f);
    }

    public async Task WaitForFrameStartAsync(
        FramePacingMode mode,
        TimeSpan previousFrameWorkTime,
        CancellationToken cancellationToken)
    {
        if (mode != FramePacingMode.VSync)
            await WaitForTargetFramePeriodAsync(previousFrameWorkTime, cancellationToken);
    }

    private async Task WaitForTargetFramePeriodAsync(TimeSpan previousFrameWorkTime, CancellationToken cancellationToken)
    {
        var delay = _targetFramePeriod - previousFrameWorkTime;
        if (delay > TimeSpan.Zero)
            await DelayPreciselyAsync(delay, cancellationToken);
    }

    private static async Task DelayPreciselyAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var targetTimestamp = Stopwatch.GetTimestamp() +
                              (long)(delay.TotalSeconds * Stopwatch.Frequency);
        var spinThreshold = Stopwatch.Frequency / 1000;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = targetTimestamp - Stopwatch.GetTimestamp();
            if (remaining <= 0)
                return;

            if (remaining > spinThreshold * 2)
            {
                var delayMilliseconds = Math.Max(1, (int)((remaining - spinThreshold) * 1000 / Stopwatch.Frequency));
                await Task.Delay(delayMilliseconds, cancellationToken);
                continue;
            }

            if (remaining > spinThreshold / 4)
                Thread.Yield();
            else
                Thread.SpinWait(64);
        }
    }
}
