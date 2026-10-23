using System.Collections.ObjectModel;
using Sacred.Core.World;

namespace Sacred.Network.Gameplay;

/// <summary>Native movement-trigger banks, insertions and deletions; does not execute OnMoveOver callbacks.</summary>
public sealed class GoldMovementTriggerReplica
{
    private readonly Dictionary<uint, GoldSectorMovementTriggers> _sectors = [];
    private SacredSectorIndex? _index;
    private SacredSectorGrid? _grid;
    private readonly List<object> _routingJournal = [];
    private readonly List<GoldMovementTriggerNameRecord> _names = [];
    public GoldMovementTriggerReplica()
    {
        Sectors = new ReadOnlyDictionary<uint, GoldSectorMovementTriggers>(_sectors);
        Names = _names.AsReadOnly();
    }
    public IReadOnlyDictionary<uint, GoldSectorMovementTriggers> Sectors { get; }
    public IReadOnlyList<GoldMovementTriggerNameRecord> Names { get; }
    public GoldMovementTriggerDeletionQueue PendingDeletions { get; } = new();
    public int PendingInsertionCount => _routingJournal.OfType<GoldMovementTriggerInsertion>().Count();
    public int AppliedInsertionCount { get; private set; }
    public int AppliedDeletionCount { get; private set; }
    public int RemovedRecordCount { get; private set; }
    public void ConfigureSectors(SacredSectorIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (_index is not null) throw new InvalidOperationException("Sector routing is already configured for this session.");
        var grid = index.CreateGrid();
        _index = index; _grid = grid;
        // Bank-only state can be shown before KEYX loads. Replay the full routing history to preserve arrival order.
        _sectors.Clear();
        foreach (var operation in _routingJournal)
            switch (operation)
            {
                case GoldWorldBankSnapshot bank: ApplySectorBank(bank); break;
                case GoldMovementTriggerInsertion insertion: Insert(insertion); break;
                case GoldMovementTriggerDeletion deletion: Delete(deletion); break;
            }
        _routingJournal.Clear(); PendingDeletions.Clear();
    }
    internal void Apply(GoldWorldBankSnapshot snapshot)
    {
        if (snapshot.Opcode != GoldMovementTriggerLayout.BankOpcode) return;
        if (_index is null) _routingJournal.Add(snapshot);
        var names = snapshot.Sections[1].Records;
        // Native bank import skips this fixed vector when the count is zero.
        if (names.Count != 0)
        {
            _names.Clear();
            _names.AddRange(names.Select(record => GoldMovementTriggerNameRecord.FromPayload(record.Data)));
        }
        ApplySectorBank(snapshot);
    }
    private void ApplySectorBank(GoldWorldBankSnapshot snapshot)
    {
        var updates = snapshot.Sections[GoldMovementTriggerLayout.BankSection].Records.Select(record =>
            (Sector: record.Key, Bank: new GoldSectorMovementTriggers(Enumerable.Range(0, record.Count)
                .Select(index => GoldMovementTriggerRecord.FromPayload(record.GetElement(index)))))).ToArray();
        // The native importer replaces only explicitly listed sector vectors. Omitted sectors retain their records.
        foreach (var update in updates) _sectors[update.Sector] = update.Bank;
    }
    internal bool Apply(GoldMovementTriggerDeletion deletion)
    {
        if (_index is null) { _routingJournal.Add(deletion); return false; }
        Delete(deletion);
        return true;
    }
    private void Delete(GoldMovementTriggerDeletion deletion)
    {
        foreach (var sector in deletion.GetTargetSectors(_index!))
            if (_sectors.TryGetValue(sector, out var bank)) RemovedRecordCount += bank.Remove(deletion);
        AppliedDeletionCount++;
    }
    internal void Apply(GoldMovementTriggerInsertionEvent entry)
    {
        var insertion = GoldMovementTriggerInsertionCodec.Decode(entry.Message).Insertion;
        _names.Add(insertion.Registration);
        if (_grid is null) _routingJournal.Add(insertion);
        else Insert(insertion);
    }
    private void Insert(GoldMovementTriggerInsertion insertion)
    {
        foreach (var piece in GoldMovementTriggerPlanner.CreateReceived(insertion, _grid!))
        {
            if (!_sectors.TryGetValue(piece.Sector, out var bank))
                _sectors[piece.Sector] = bank = new GoldSectorMovementTriggers([]);
            bank.Add(piece.Record);
        }
        AppliedInsertionCount++;
    }
    internal void Clear()
    {
        _sectors.Clear(); _index = null; _grid = null; _names.Clear(); _routingJournal.Clear(); PendingDeletions.Clear();
        AppliedDeletionCount = 0; RemovedRecordCount = 0; AppliedInsertionCount = 0;
    }
}
