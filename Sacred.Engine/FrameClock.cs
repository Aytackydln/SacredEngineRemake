using System;
using System.Diagnostics;
using System.Threading;
using Sacred.Engine.Extern;

namespace Sacred.Engine;

public sealed class HighResolutionFrameClock : IDisposable
{
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerModifyState = 0x0002;
    private const uint Synchronize = 0x00100000;

    private readonly nint _timer;
    private long _last = Stopwatch.GetTimestamp();
    private readonly long _targetFramePeriodTicks;
    private long _nextFrameTimestamp;

    public HighResolutionFrameClock(uint targetFrameRate)
    {
        TargetFrameRate = Math.Clamp(targetFrameRate, 30u, 1000u);
        _targetFramePeriodTicks = Math.Max(1, Stopwatch.Frequency / TargetFrameRate);
        _timer = Kernel32.CreateWaitableTimerEx(
            0,
            null,
            CreateWaitableTimerHighResolution,
            TimerModifyState | Synchronize);
        if (_timer == 0)
        {
            _timer = Kernel32.CreateWaitableTimerEx(
                0,
                null,
                0,
                TimerModifyState | Synchronize);
        }

        if (_timer == 0)
            throw new InvalidOperationException("Failed to create the frame pacing timer.");
    }

    public uint TargetFrameRate { get; }

    public float Tick()
    {
        var now = Stopwatch.GetTimestamp();
        var dt = (float)((now - _last) / (double)Stopwatch.Frequency);
        _last = now;
        return Math.Clamp(dt, 0.0f, 0.1f);
    }

    public void WaitForFrameStart(bool frameRateLimited, CancellationToken cancellationToken)
    {
        if (!frameRateLimited)
        {
            _nextFrameTimestamp = 0;
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (_nextFrameTimestamp == 0)
        {
            _nextFrameTimestamp = now;
            return;
        }

        var deadline = _nextFrameTimestamp + _targetFramePeriodTicks;
        if (deadline <= now)
        {
            _nextFrameTimestamp = now;
            return;
        }

        _nextFrameTimestamp = deadline;
        WaitPrecisely(deadline, cancellationToken);
    }

    public void ResetPacing() => _nextFrameTimestamp = 0;

    public void Dispose() => Kernel32.CloseHandle(_timer);

    private void WaitPrecisely(long targetTimestamp, CancellationToken cancellationToken)
    {
        // A high-resolution kernel timer does almost all of the waiting without occupying a
        // worker thread. Only the final 50 microseconds are spun to avoid scheduler jitter.
        var spinThreshold = Math.Max(1, Stopwatch.Frequency / 20_000);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = targetTimestamp - Stopwatch.GetTimestamp();
            if (remaining <= 0)
                return;

            if (remaining > spinThreshold)
            {
                var waitTicks = remaining - spinThreshold;
                var dueTime = -Math.Max(1L, waitTicks * 10_000_000L / Stopwatch.Frequency);
                if (!Kernel32.SetWaitableTimerEx(_timer, in dueTime, 0, 0, 0, 0, 0))
                    throw new InvalidOperationException("Failed to arm the frame pacing timer.");

                if (Kernel32.WaitForSingleObject(_timer, Kernel32.Infinite) != Kernel32.WaitObject0)
                    throw new InvalidOperationException("Failed while waiting for the frame pacing timer.");
                continue;
            }

            Thread.SpinWait(16);
        }
    }
}
