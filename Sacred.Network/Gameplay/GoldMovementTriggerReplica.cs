using System.Collections.ObjectModel;
using Sacred.Core.World;

namespace Sacred.Network.Gameplay;

/// <summary>Native movement-trigger banks and applied deletions; does not install or execute OnMoveOver callbacks.</summary>
public sealed class GoldMovementTriggerReplica
{
    private readonly Dictionary<uint, GoldSectorMovementTriggers> _sectors = [];
    private SacredSectorIndex? _index;
    public GoldMovementTriggerReplica() => Sectors = new ReadOnlyDictionary<uint, GoldSectorMovementTriggers>(_sectors);
    public IReadOnlyDictionary<uint, GoldSectorMovementTriggers> Sectors { get; }
    public int AppliedDeletionCount { get; private set; }
    public int RemovedRecordCount { get; private set; }
    public void ConfigureSectors(SacredSectorIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (_sectors.Count != 0 || AppliedDeletionCount != 0) throw new InvalidOperationException("Configure sector routing before movement-trigger events.");
        _index = index;
    }
    internal void Apply(GoldWorldBankSnapshot snapshot)
    {
        if (snapshot.Opcode != GoldMovementTriggerLayout.BankOpcode) return;
        var updates = snapshot.Sections[GoldMovementTriggerLayout.BankSection].Records.Select(record =>
            (Sector: record.Key, Bank: new GoldSectorMovementTriggers(Enumerable.Range(0, record.Count)
                .Select(index => GoldMovementTriggerRecord.FromPayload(record.GetElement(index)))))).ToArray();
        // The native importer replaces only explicitly listed sector vectors. Omitted sectors retain their records.
        foreach (var update in updates) _sectors[update.Sector] = update.Bank;
    }
    internal bool Apply(GoldMovementTriggerDeletion deletion)
    {
        if (_index is null) return false;
        foreach (var sector in deletion.GetTargetSectors(_index))
            if (_sectors.TryGetValue(sector, out var bank)) RemovedRecordCount += bank.Remove(deletion);
        AppliedDeletionCount++;
        return true;
    }
    internal void Clear()
    {
        _sectors.Clear(); _index = null;
        AppliedDeletionCount = 0; RemovedRecordCount = 0;
    }
}
