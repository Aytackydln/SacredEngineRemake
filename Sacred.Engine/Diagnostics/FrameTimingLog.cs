using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Sacred.Engine.Diagnostics;

/// <summary>Reports frame throughput and where the frame loop spends time.</summary>
internal sealed class FrameTimingLog
{
    public AnimationPerformanceLog Animation { get; } = new();
    private long _start;
    private int _count;
    private double _wait;
    private double _update;
    private double _present;
    private bool _enabled;
    private readonly List<double> _activeTimes = [];

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            _start = 0;
            _activeTimes.Clear();
            _count = 0;
            _wait = _update = _present = 0;
        }
    }

    public void Record(double wait, double update, double present, string context)
    {
        Animation.RecordFrame();
        if (!Enabled)
        {
            _start = 0;
            _activeTimes.Clear();
            _count = 0;
            _wait = _update = _present = 0;
            return;
        }

        if (_start == 0)
            _start = Stopwatch.GetTimestamp();
        _count++;
        _wait += wait;
        _update += update;
        _present += present;
        _activeTimes.Add(update + present);
        var elapsed = Stopwatch.GetElapsedTime(_start).TotalSeconds;
        if (elapsed < 2)
            return;

        _activeTimes.Sort();
        var median = _activeTimes[_activeTimes.Count / 2];
        var tail = _activeTimes[Math.Min(_activeTimes.Count - 1, (int)(_activeTimes.Count * .95))];
        EngineLog.WriteLine($"Frame timing: {_count / elapsed:0.0} FPS; wait {_wait / _count:0.00} ms; update/render {_update / _count:0.00} ms; present {_present / _count:0.00} ms; active median {median:0.000} ms; p95 {tail:0.000} ms; {context}");
        _activeTimes.Clear();
        _start = 0;
        _count = 0;
        _wait = _update = _present = 0;
    }
}
