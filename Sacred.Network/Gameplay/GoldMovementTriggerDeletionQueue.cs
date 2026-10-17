namespace Sacred.Network.Gameplay;

/// <summary>Retain ordered native deletion work until world-sector trigger definitions are bound.</summary>
public sealed class GoldMovementTriggerDeletionQueue
{
    private readonly List<GoldMovementTriggerDeletion> _instructions = [];
    public GoldMovementTriggerDeletionQueue() => Instructions = _instructions.AsReadOnly();
    public IReadOnlyList<GoldMovementTriggerDeletion> Instructions { get; }
    public void Add(GoldMovementTriggerDeletion deletion)
    {
        ArgumentNullException.ThrowIfNull(deletion);
        _instructions.Add(deletion);
    }
    public void Clear() => _instructions.Clear();
}
