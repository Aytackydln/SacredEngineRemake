using System.Diagnostics;
using System.Threading;

namespace Sacred.Engine.Assets;

/// <summary>One-time CPU composition work on asset workers, excluding archive I/O and uploads.</summary>
internal static class StaticSpriteCompositionStatistics
{
    private static long _ticks;
    private static long _parts;
    private static long _frames;
    public static void Record(long started, int parts)
    {
        Interlocked.Add(ref _ticks, Stopwatch.GetTimestamp() - started);
        Interlocked.Add(ref _parts, parts);
        Interlocked.Increment(ref _frames);
    }
    public static string Status => $"source composition {Interlocked.Read(ref _ticks) * 1000.0 / Stopwatch.Frequency:F3} ms total, " +
        $"{Interlocked.Read(ref _parts)} parts into {Interlocked.Read(ref _frames)} frames";
}
