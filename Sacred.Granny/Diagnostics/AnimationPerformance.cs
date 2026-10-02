using System.Diagnostics;

namespace Sacred.Granny.Diagnostics;

public enum AnimationCpuStage { Sampling, Palettes, Skinning, Bounds, Effects, VertexUpload, PaletteUpload }

/// <summary>Optional renderer-independent counters. Disabled scopes do not read the clock.</summary>
public static class AnimationPerformance
{
    private static readonly long[] Ticks = new long[Enum.GetValues<AnimationCpuStage>().Length];
    private static readonly long[] Calls = new long[Ticks.Length];
    private static long _uploadedBytes;
    private static long _paletteBytes;
    public static bool Enabled { get; set; }

    public static Scope Measure(AnimationCpuStage stage) => Enabled ? new(stage, Stopwatch.GetTimestamp()) : default;
    public static void RecordVertexUpload(int bytes)
    {
        if (Enabled) Interlocked.Add(ref _uploadedBytes, bytes);
    }
    public static void RecordPaletteUpload(int bytes) { if (Enabled) Interlocked.Add(ref _paletteBytes, bytes); }

    public static AnimationPerformanceSnapshot CaptureAndReset()
    {
        var milliseconds = new double[Ticks.Length];
        var calls = new long[Calls.Length];
        for (var i = 0; i < Ticks.Length; i++)
        {
            milliseconds[i] = Interlocked.Exchange(ref Ticks[i], 0) * 1000.0 / Stopwatch.Frequency;
            calls[i] = Interlocked.Exchange(ref Calls[i], 0);
        }
        return new(milliseconds, calls, Interlocked.Exchange(ref _uploadedBytes, 0))
        { UploadedPaletteBytes = Interlocked.Exchange(ref _paletteBytes, 0) };
    }

    public readonly struct Scope(AnimationCpuStage stage, long start) : IDisposable
    {
        public void Dispose()
        {
            if (start == 0) return;
            Interlocked.Add(ref Ticks[(int)stage], Stopwatch.GetTimestamp() - start);
            Interlocked.Increment(ref Calls[(int)stage]);
        }
    }
}

public sealed record AnimationPerformanceSnapshot(double[] Milliseconds, long[] Calls, long UploadedVertexBytes)
{ public long UploadedPaletteBytes { get; init; } }
