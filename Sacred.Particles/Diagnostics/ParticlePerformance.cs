using System.Diagnostics;

namespace Sacred.Particles.Diagnostics;

public enum ParticleCpuStage { Births, SpawnPreAdvance, LifetimeMirror, ControlPackets, Halos, SimulationStorage }

/// <summary>Renderer-independent particle counters. Synchronous nested timing/allocation
/// scopes overlap and must not be added together.</summary>
public static class ParticlePerformance
{
    private static readonly long[] Ticks = new long[Enum.GetValues<ParticleCpuStage>().Length];
    private static readonly long[] Calls = new long[Ticks.Length];
    private static readonly long[] Allocations = new long[Ticks.Length];
    private static long _births;
    public static bool Enabled { get; set; }
    public static Scope Measure(ParticleCpuStage stage) => Enabled
        ? new(stage, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread()) : default;
    public static void RecordBirths(int count)
    {
        if (Enabled) Interlocked.Add(ref _births, count);
    }
    public static ParticlePerformanceSnapshot CaptureAndReset()
    {
        var milliseconds = new double[Ticks.Length];
        var calls = new long[Ticks.Length];
        var allocated = new long[Ticks.Length];
        for (var i = 0; i < Ticks.Length; i++)
        {
            milliseconds[i] = Interlocked.Exchange(ref Ticks[i], 0) * 1000.0 / Stopwatch.Frequency;
            calls[i] = Interlocked.Exchange(ref Calls[i], 0);
            allocated[i] = Interlocked.Exchange(ref Allocations[i], 0);
        }
        return new(milliseconds, calls, allocated, Interlocked.Exchange(ref _births, 0));
    }
    public readonly struct Scope(ParticleCpuStage stage, long start, long allocationStart) : IDisposable
    {
        public void Dispose()
        {
            if (start == 0) return;
            Interlocked.Add(ref Ticks[(int)stage], Stopwatch.GetTimestamp() - start);
            Interlocked.Increment(ref Calls[(int)stage]);
            Interlocked.Add(ref Allocations[(int)stage], GC.GetAllocatedBytesForCurrentThread() - allocationStart);
        }
    }
}

public sealed record ParticlePerformanceSnapshot(double[] Milliseconds, long[] Calls,
    long[] AllocatedBytes, long Births);
