using System.Collections.Frozen;

namespace Sacred.Network.Gameplay;

/// <summary>A deferred tile mark retains the native entered-sector flags at packet receipt.</summary>
public sealed record GoldMouseoverTileWork(GoldSectorScriptPatch Patch, IReadOnlySet<ushort> EnteredSectors);

/// <summary>Native spawn-renew vector, mouseover registry and pending sector registrations.</summary>
public sealed class GoldScriptRegistrationReplica
{
    private readonly List<GoldSpawnRenewRecord> _spawnRenewals = [];
    private readonly List<GoldSectorScriptPatch> _pending = [];
    private readonly List<GoldSectorScriptPatch> _incremental = [];
    private readonly Queue<GoldMouseoverTileWork> _tiles = [];
    public GoldScriptRegistrationReplica()
    {
        SpawnRenewals = _spawnRenewals.AsReadOnly(); PendingMouseovers = _pending.AsReadOnly();
        IncrementalMouseovers = _incremental.AsReadOnly();
    }
    public IReadOnlyList<GoldSpawnRenewRecord> SpawnRenewals { get; }
    public IReadOnlyList<GoldSectorScriptPatch> PendingMouseovers { get; }
    public IReadOnlyList<GoldSectorScriptPatch> IncrementalMouseovers { get; }
    public GoldMouseoverRegistry Mouseovers { get; } = new();
    public int PendingTileCount => _tiles.Count;
    public bool TryPeekTile(out GoldMouseoverTileWork? work) => _tiles.TryPeek(out work);
    public void CompleteTile(GoldMouseoverTileWork work)
    {
        if (!_tiles.TryPeek(out var head) || !ReferenceEquals(head, work))
            throw new InvalidOperationException("Mouseover tile completion requires the current queued receipt.");
        _tiles.Dequeue();
    }
    internal void Apply(GoldScriptRegistrationEvent entry, GoldSectorEntryRuntime? runtime)
    {
        switch (GoldScriptRegistrationCodec.Decode(entry.Message))
        {
            case GoldSpawnRenewEvent spawn: _spawnRenewals.Add(spawn.Record); break;
            case GoldMouseoverEvent mouseover:
                var patch = mouseover.Record;
                Mouseovers.Register(patch);
                if (!_pending.Any(p => p.X == patch.X && p.Y == patch.Y && p.Surface == patch.Surface))
                { _pending.Add(patch); _incremental.Add(patch); }
                var entered = runtime?.Flags.Where(pair => (pair.Value & GoldSectorEntryRuntime.EnteredFlag) != 0)
                    .Select(pair => pair.Key).ToFrozenSet() ?? FrozenSet<ushort>.Empty;
                _tiles.Enqueue(new(patch, entered));
                break;
        }
    }
    internal void Apply(GoldWorldBankSnapshot bank)
    {
        if (bank.Opcode == GoldWorldBankLayout.Bank2Opcode)
        {
            var records = bank.Sections[GoldScriptRegistrationLayout.SpawnRenewBankSection].Records;
            if (records.Count == 0) return; // Native 0x7E6164 leaves the vector unchanged.
            _spawnRenewals.Clear();
            _spawnRenewals.AddRange(records.Select(row => GoldSpawnRenewRecord.FromPayload(row.Data)));
        }
        if (bank.Opcode == GoldWorldBankLayout.Bank3Opcode)
        {
            var records = bank.Sections[GoldScriptRegistrationLayout.MouseoverBankSection].Records;
            if (records.Count == 0) return; // Native 0x7E6F3C skips the import.
            _pending.Clear(); _incremental.Clear();
            _pending.AddRange(records.Select(row => GoldSectorScriptPatch.FromPayload(row.Data)));
        }
    }
    internal void Clear()
    {
        _spawnRenewals.Clear(); _pending.Clear(); _incremental.Clear(); _tiles.Clear(); Mouseovers.Clear();
    }
}
