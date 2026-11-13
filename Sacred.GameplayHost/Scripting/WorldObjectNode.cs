namespace Sacred.GameplayHost.Scripting;

/// <summary>Initial object's native cNode header and activation record.
/// Node activation is independent of physical model-loaded flag 0x04000000.</summary>
public sealed class WorldObjectNode
{
    public byte Flags { get; internal set; } = 1;
    public byte QueueKind { get; init; } = 1;
    public uint PeriodMilliseconds { get; init; } = 50;
    public bool Active => (Flags & 0x80) != 0;
    public double StartedAt { get; internal set; }
    public double NextDue { get; internal set; }
}
