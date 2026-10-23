using Sacred.Core.GameBin.Scripts;

namespace Sacred.Network.Gameplay;

/// <summary>Ordered received definition state and changes for a script context that also makes local mutations.</summary>
public sealed class GoldScriptDefinitionReplica
{
    private readonly List<SacredScriptDefinition> _records = [];
    private readonly Queue<GoldScriptDefinitionChange> _changes = [];
    private readonly List<GoldScriptDefinitionEvent> _pending = [];
    public GoldScriptDefinitionReplica()
    {
        Records = _records.AsReadOnly();
    }
    public IReadOnlyList<SacredScriptDefinition> Records { get; }
    public int PendingChangeCount => _changes.Count;
    public bool TryPeekChange(out GoldScriptDefinitionChange? change) => _changes.TryPeek(out change);
    public void CompleteChange(GoldScriptDefinitionChange change)
    {
        if (!_changes.TryPeek(out var head) || !ReferenceEquals(head, change))
            throw new InvalidOperationException("Definition completion requires the current queued change.");
        _changes.Dequeue();
    }
    public bool IsInitialized { get; private set; }
    public int PendingCount => _pending.Count;

    /// <summary>Original campaign baseline, used only if a nonempty host bank has not already supplied newer state.</summary>
    public void Configure(IEnumerable<SacredScriptDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (IsInitialized) return;
        var copy = definitions.ToArray();
        Replace(copy);
        foreach (var entry in _pending) ApplyKnown(entry);
        _pending.Clear();
    }
    internal void Apply(GoldScriptDefinitionEvent entry)
    {
        var decoded = GoldScriptDefinitionCodec.Decode(entry.Message);
        if (!IsInitialized) _pending.Add(decoded);
        else ApplyKnown(decoded);
    }
    private void ApplyKnown(GoldScriptDefinitionEvent entry)
    {
        ApplyTo(_records, entry);
        // Even a locally ignored index must reach a context whose scripts may have added records.
        _changes.Enqueue(new(entry, null));
    }
    internal void Apply(GoldWorldBankSnapshot bank)
    {
        if (bank.Opcode != GoldWorldBankLayout.Bank2Opcode || bank.Sections[5].Count == 0) return;
        var copy = bank.Sections[5].Records.Select(row => SacredScriptDefinition.FromPayload(row.Data)).ToArray();
        Replace(copy);
        _pending.Clear(); // A later snapshot supersedes earlier updates awaiting their baseline.
    }
    private void Replace(SacredScriptDefinition[] records)
    {
        _records.Clear(); _records.AddRange(records); IsInitialized = true;
        _changes.Enqueue(new(null, Array.AsReadOnly(records)));
    }
    /// <summary>Native indexed append/replace and reverse exact-key deletion, without erasing unrelated script mutations.</summary>
    public static void ApplyTo(IList<SacredScriptDefinition> records, GoldScriptDefinitionEvent entry)
    {
        ArgumentNullException.ThrowIfNull(records);
        switch (GoldScriptDefinitionCodec.Decode(entry.Message))
        {
            case GoldScriptDefinitionUpdateEvent update:
                if (update.Index == GoldScriptDefinitionCodec.AppendIndex) records.Add(update.Definition);
                else if (update.Index < records.Count) records[(int)update.Index] = update.Definition;
                break;
            case GoldScriptDefinitionDeleteEvent deletion:
                // Gold starts at the last record and stops before slot zero, including a one-record table.
                for (var index = records.Count - 1; index > 0; index--)
                {
                    var record = records[index];
                    if (record.Kind != deletion.Kind || record.NameStorage.IndexOf((byte)0) < 0 ||
                        !string.Equals(record.Name, deletion.Name, StringComparison.Ordinal)) continue;
                    records.RemoveAt(index); // Native shifts the following records, preserving order.
                    break;
                }
                break;
        }
    }
    internal void Clear()
    {
        _records.Clear(); _changes.Clear(); _pending.Clear(); IsInitialized = false;
    }
}

public sealed record GoldScriptDefinitionChange(GoldScriptDefinitionEvent? Event, IReadOnlyList<SacredScriptDefinition>? Snapshot);
