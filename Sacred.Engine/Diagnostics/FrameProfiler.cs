using System;
using System.Diagnostics;

namespace Sacred.Engine.Diagnostics;

/// <summary>Per-frame CPU timing regions for the engine's main thread, shown in the debug UI.
/// Regions use <see cref="Stopwatch.GetTimestamp"/> / <see cref="Stopwatch.GetElapsedTime(long)"/>.
/// Not thread-safe by design: record only from the thread that runs the frame loop. A stage hit several
/// times in one frame accumulates. Disabled regions cost one branch and never read the clock.</summary>
internal static class FrameProfiler
{
    /// <summary>Frames of history kept per stage (about four seconds at 60 FPS).</summary>
    public const int HistoryLength = 240;

    private static readonly double[] Current = new double[FrameStages.Count];
    // Flat ring buffers, one row per stage, so ImGui can plot a row directly.
    private static readonly float[] History = new float[FrameStages.Count * HistoryLength];
    private static int _writeIndex;
    private static int _framesRecorded;

    public static bool Enabled { get; set; } = true;
    public static int FramesRecorded => _framesRecorded;
    /// <summary>Index the next sample will be written to; pass as the plot offset so the oldest sample is first.</summary>
    public static int HistoryOffset => _writeIndex;

    /// <summary>Returns a start timestamp, or 0 when profiling is off (<see cref="End"/> ignores 0).</summary>
    public static long Begin() => Enabled ? Stopwatch.GetTimestamp() : 0;

    public static void End(FrameStage stage, long start)
    {
        if (start != 0)
            Current[(int)stage] += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    public static Scope Measure(FrameStage stage) => new(stage, Begin());

    /// <summary>For waits that are already measured elsewhere and only need to be reported.</summary>
    public static void AddMilliseconds(FrameStage stage, double milliseconds)
    {
        if (Enabled)
            Current[(int)stage] += milliseconds;
    }

    /// <summary>Closes the frame: publishes this frame's totals into the history and clears the accumulators.</summary>
    public static void EndFrame(double frameIntervalMilliseconds)
    {
        if (!Enabled)
        {
            Array.Clear(Current);
            return;
        }
        Current[(int)FrameStage.FrameInterval] = frameIntervalMilliseconds;
        for (var stage = 0; stage < Current.Length; stage++)
        {
            History[stage * HistoryLength + _writeIndex] = (float)Current[stage];
            Current[stage] = 0;
        }
        _writeIndex = (_writeIndex + 1) % HistoryLength;
        _framesRecorded = Math.Min(_framesRecorded + 1, HistoryLength);
    }

    public static void Reset()
    {
        Array.Clear(Current);
        Array.Clear(History);
        _writeIndex = 0;
        _framesRecorded = 0;
    }

    /// <summary>The stage's history row in ring order; pair with <see cref="HistoryOffset"/> for plotting.</summary>
    public static ReadOnlySpan<float> HistoryOf(FrameStage stage) =>
        History.AsSpan((int)stage * HistoryLength, HistoryLength);

    /// <summary>First element of the stage history row, for plot APIs that take a reference. Treat as read-only.</summary>
    public static ref float HistoryRow(FrameStage stage) => ref History[(int)stage * HistoryLength];

    public static float LastOf(FrameStage stage) =>
        _framesRecorded == 0 ? 0 : History[(int)stage * HistoryLength + (_writeIndex + HistoryLength - 1) % HistoryLength];

    /// <summary>Average, maximum and 95th percentile over the recorded frames. Sorts a scratch copy, so call at UI rates only.</summary>
    public static StageStatistics Statistics(FrameStage stage, Span<float> scratch)
    {
        var count = _framesRecorded;
        if (count == 0 || scratch.Length < count)
            return default;
        var row = HistoryOf(stage);
        // Until the ring wraps, samples occupy [0, count); afterwards the whole row is valid.
        var samples = scratch[..count];
        row[..count].CopyTo(samples);
        var sum = 0.0;
        var max = 0f;
        foreach (var value in samples)
        {
            sum += value;
            if (value > max) max = value;
        }
        samples.Sort();
        return new StageStatistics((float)(sum / count), max, samples[Math.Min(count - 1, (int)(count * 0.95))]);
    }

    public readonly struct Scope(FrameStage stage, long start) : IDisposable
    {
        public void Dispose() => End(stage, start);
    }
}

internal readonly record struct StageStatistics(float AverageMilliseconds, float MaximumMilliseconds, float P95Milliseconds);
