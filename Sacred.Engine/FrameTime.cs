using System.Diagnostics;

namespace Sacred.Engine;

/// <summary>The time of one frame, captured once at the start of the frame. Everything that depends on time while the
/// frame is built (shader animation clocks, overlay animation, ...) reads this instead of the clock, so every draw in
/// a frame sees the same instant. It is elapsed real time and a per-frame delta, never a frame counter, so animation
/// speed does not depend on the frame rate.</summary>
/// <param name="FrameId">Index of the frame this snapshot belongs to.</param>
/// <param name="Timestamp"><see cref="Stopwatch.GetTimestamp"/> value taken at the start of the frame.</param>
/// <param name="Seconds">Seconds from the source's creation to <paramref name="Timestamp"/>.</param>
/// <param name="DeltaSeconds">Seconds since the previous frame started (clamped by the frame clock).</param>
public readonly record struct FrameTime(ulong FrameId, long Timestamp, double Seconds, float DeltaSeconds)
{
    /// <summary>Whole milliseconds, wrapping like the 32-bit counters the native overlay animations use.</summary>
    public uint Milliseconds => unchecked((uint)(long)(Seconds * 1000.0));
}

/// <summary>Owns the current <see cref="FrameTime"/>. The frame loop calls <see cref="Begin"/> exactly once per frame;
/// consumers only read <see cref="Current"/>.</summary>
public sealed class FrameTimeSource
{
    private readonly long _origin = Stopwatch.GetTimestamp();

    /// <summary>Time of the frame being built. Before the first frame it is zero.</summary>
    public FrameTime Current { get; private set; }

    public void Begin(ulong frameId, long timestamp, float deltaSeconds) =>
        Current = new FrameTime(frameId, timestamp, (timestamp - _origin) / (double)Stopwatch.Frequency, deltaSeconds);
}
