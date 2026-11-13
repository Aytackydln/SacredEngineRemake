namespace Sacred.GameplayHost.Scripting;

/// <summary>Native 32-byte queue record's logical fields. Deactivation leaves
/// a tombstone until the scheduler compacts its vector; times stay unchanged.</summary>
public sealed class WorldObjectActivationRecord(WorldObjectNode node)
{
    public WorldObjectNode? Node { get; internal set; } = node;
    public byte Flags { get; internal set; }
    public double StartedAt { get; } = node.StartedAt;
    public double NextDue { get; } = node.NextDue;
}
