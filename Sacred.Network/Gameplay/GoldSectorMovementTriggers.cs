namespace Sacred.Network.Gameplay;

/// <summary>Mutable native sector vector with immutable records. Deletion uses swap-with-last order.</summary>
public sealed class GoldSectorMovementTriggers
{
    private readonly List<GoldMovementTriggerRecord> _records;
    internal GoldSectorMovementTriggers(IEnumerable<GoldMovementTriggerRecord> records)
    {
        _records = records.ToList();
        Records = _records.AsReadOnly();
    }
    public IReadOnlyList<GoldMovementTriggerRecord> Records { get; }
    internal int Remove(GoldMovementTriggerDeletion deletion)
    {
        var removed = 0;
        for (var index = 0; index < _records.Count;)
        {
            if (!deletion.MatchesName(_records[index].NameStorage)) { index++; continue; }
            _records[index] = _records[^1];
            _records.RemoveAt(_records.Count - 1);
            removed++;
        }
        return removed;
    }
}
