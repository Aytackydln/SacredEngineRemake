using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

/// <summary>Ordered server-requested scripts, initialization and zero-mode tile passes. Receiving terrain or object bytes does not
/// complete this queue. Revisit/duplicate rules belong to the native world executor.</summary>
public sealed class GoldSectorWorkQueue
{
    private readonly Dictionary<long, GoldSectorWork> _work = [];
    private readonly Dictionary<ushort, long> _latest = [];
    private long _next;
    private long _reply;
    public GoldSectorWorkQueue() => Work = new ReadOnlyDictionary<long, GoldSectorWork>(_work);
    public IReadOnlyDictionary<long, GoldSectorWork> Work { get; }
    public GoldSectorEntryRuntime Runtime { get; } = new();
    public long Session { get; private set; }
    public int PendingCount => _work.Values.Count(work => work.State is GoldSectorWorkState.Queued or GoldSectorWorkState.Executing);
    public int FailedCount => _work.Values.Count(work => work.State == GoldSectorWorkState.Failed);
    public bool IsComplete => PendingCount == 0 && FailedCount == 0;
    /// <summary>A neighboring pending operation does not revoke completed work for this sector.
    /// Re-entry requests replace its previous completion until their own work finishes.</summary>
    public bool IsSectorComplete(ushort sector) => _latest.TryGetValue(sector, out var id) &&
        _work[id].State is GoldSectorWorkState.Completed or GoldSectorWorkState.Skipped;

    internal void Enqueue(GoldSectorEntry entry, IReadOnlyDictionary<ushort, GoldWorldBankSnapshot> banks)
    {
        var reply = ++_reply;
        for (var index = 0; index < entry.Count; index++)
        {
            var slot = entry.GetSlot(index);
            if (slot.Sector == 0) continue;
            var executeScripts = Runtime.Receive(slot);
            var id = ++_next;
            _latest[slot.Sector] = id;
            _work.Add(id, new(Session, id, reply, index, slot,
                slot.ScriptMode != 0 && !executeScripts && !slot.RequestsInitialization ? GoldSectorWorkState.Skipped : GoldSectorWorkState.Queued)
                { ExecuteScripts = executeScripts,
                    PatchBank = banks.GetValueOrDefault(GoldWorldBankLayout.Bank3Opcode),
                    PositionBank = banks.GetValueOrDefault(GoldWorldBankLayout.Bank1Opcode) });
        }
    }

    /// <summary>Serial execution preserves the native nine-slot receive loop order.</summary>
    public GoldSectorWork? BeginNext()
    {
        if (_work.Values.Any(work => work.State is GoldSectorWorkState.Executing or GoldSectorWorkState.Failed)) return null;
        foreach (var work in _work.Values.OrderBy(work => work.Id))
            if (work.State == GoldSectorWorkState.Queued)
            {
                var executing = work with { State = GoldSectorWorkState.Executing };
                _work[work.Id] = executing;
                return executing;
            }
        return null;
    }

    /// <returns>False when this completion belongs to a retired session. A live invalid transition throws.</returns>
    public bool Complete(GoldSectorWork executing) => Finish(executing, null);
    public bool Fail(GoldSectorWork executing, string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return Finish(executing, error);
    }
    private bool Finish(GoldSectorWork executing, string? error)
    {
        ArgumentNullException.ThrowIfNull(executing);
        if (executing.Session != Session) return false;
        if (!_work.TryGetValue(executing.Id, out var current) || !ReferenceEquals(current, executing) ||
            current.State != GoldSectorWorkState.Executing)
            throw new InvalidOperationException("Sector completion requires the current executing operation from this queue.");
        _work[executing.Id] = current with { State = error is null ? GoldSectorWorkState.Completed : GoldSectorWorkState.Failed, Error = error };
        return true;
    }
    internal void Clear()
    {
        _work.Clear();
        _latest.Clear();
        Runtime.Clear();
        Session++;
        _next = 0;
        _reply = 0;
    }
}
