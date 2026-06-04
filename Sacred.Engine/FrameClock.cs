using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sacred.Engine;

public sealed partial class FrameClock
{
    private long _last = Stopwatch.GetTimestamp();
    private long _lastPresent = Stopwatch.GetTimestamp();
    private readonly long _fallbackFrameTicks = Stopwatch.Frequency / 60;

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

    [LibraryImport("dwmapi")]
    private static partial int DwmFlush();
}
