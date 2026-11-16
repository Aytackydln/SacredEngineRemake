namespace Sacred.GameplayHost.Scripting;

/// <summary>Caller-owned delayed pairs and native last-sweep word 0xAD5C4C.
/// This view introduces no separate actor or grant registry.</summary>
public sealed class WorldObjectReferenceMaintenanceState(IList<WorldObjectReferenceDelay> records, uint lastSweep)
{
    public IList<WorldObjectReferenceDelay> Records { get; } = records ?? throw new ArgumentNullException(nameof(records));
    public uint LastSweep { get; set; } = lastSweep;
}
