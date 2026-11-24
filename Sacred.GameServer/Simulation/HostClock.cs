using System.Diagnostics;

namespace Sacred.GameServer.Simulation;

/// <summary>The game loop's clock. Real servers run on a stopwatch; a capture replay (<see cref="CaptureReplay"/>) sets the time
/// itself so the server sees the session's own timing.</summary>
public class HostClock
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    public virtual long ElapsedMilliseconds => _watch.ElapsedMilliseconds;
}

/// <summary>A clock that only moves when told to.</summary>
public sealed class ManualClock : HostClock
{
    public long Now { get; set; }
    public override long ElapsedMilliseconds => Now;
}
