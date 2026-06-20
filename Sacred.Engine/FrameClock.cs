using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Sacred.Engine;

public sealed class FrameClock
{
    private long _last = Stopwatch.GetTimestamp();
    private readonly long _targetFramePeriodTicks;
    private long _nextFrameTimestamp;

    public FrameClock(uint targetFrameRate)
    {
        TargetFrameRate = Math.Clamp(targetFrameRate, 30u, 1000u);
        _targetFramePeriodTicks = Math.Max(1, Stopwatch.Frequency / TargetFrameRate);
    }

    public uint TargetFrameRate { get; }

    public float Tick()
    {
        var now = Stopwatch.GetTimestamp();
        var dt = (float)((now - _last) / (double)Stopwatch.Frequency);
        _last = now;
        return Math.Clamp(dt, 0.0f, 0.1f);
    }

    public ValueTask WaitForFrameStartAsync(FramePacingMode mode, CancellationToken cancellationToken)
    {
        if (mode == FramePacingMode.VSync)
        {
            _nextFrameTimestamp = 0;
            return ValueTask.CompletedTask;
        }

        var now = Stopwatch.GetTimestamp();
        if (_nextFrameTimestamp == 0)
        {
            _nextFrameTimestamp = now;
            return ValueTask.CompletedTask;
        }

        var deadline = _nextFrameTimestamp + _targetFramePeriodTicks;
        if (now - deadline > _targetFramePeriodTicks)
            deadline = now;

        _nextFrameTimestamp = deadline;
        return deadline <= now
            ? ValueTask.CompletedTask
            : DelayPreciselyAsync(deadline, cancellationToken);
    }

    private static async ValueTask DelayPreciselyAsync(long targetTimestamp, CancellationToken cancellationToken)
    {
        // Leave only the final half millisecond to cooperative yielding/spinning. This keeps
        // 240-1000 Hz caps precise without burning an entire core between frames.
        var spinThreshold = Math.Max(1, Stopwatch.Frequency / 2000);
        var minimumSchedulerDelay = Math.Max(1, Stopwatch.Frequency / 1000);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = targetTimestamp - Stopwatch.GetTimestamp();
            if (remaining <= 0)
                return;

            if (remaining > spinThreshold + minimumSchedulerDelay)
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
