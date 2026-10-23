using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

/// <summary>Native keyed script rectangles. Audio, region execution and outgoing publication remain separate.</summary>
public sealed class GoldScriptRegionReplica
{
    private readonly Dictionary<uint, List<GoldScriptRegionRecord>> _records = [];
    private readonly Dictionary<uint, IReadOnlyList<GoldScriptRegionRecord>> _views = [];
    public GoldScriptRegionReplica()
    {
        Regions = new ReadOnlyDictionary<uint, IReadOnlyList<GoldScriptRegionRecord>>(_views);
    }
    public IReadOnlyDictionary<uint, IReadOnlyList<GoldScriptRegionRecord>> Regions { get; }
    internal void Apply(GoldScriptRegionEvent entry)
    {
        var update = GoldScriptRegionCodec.Decode(entry.Message);
        if (update.Mode != 0)
        {
            var bothMissing = !_records.ContainsKey(update.FirstKey) && !_records.ContainsKey(update.SecondKey);
            Append(update.FirstKey, update.Region);
            if (update.SecondKey != update.FirstKey)
            {
                // The native temporary vector is reused between new keys: the second receives two copies.
                Append(update.SecondKey, update.Region);
                if (bothMissing) Append(update.SecondKey, update.Region);
            }
            return;
        }
        Remove(update.FirstKey);
        if (update.FirstKey != update.SecondKey) Remove(update.SecondKey);
    }
    private void Append(uint key, GoldScriptRegionRecord record)
    {
        if (!_records.TryGetValue(key, out var rows))
        {
            rows = []; _records.Add(key, rows); _views.Add(key, rows.AsReadOnly());
        }
        rows.Add(record);
    }
    private void Remove(uint key)
    {
        if (!_records.TryGetValue(key, out var rows)) return;
        if (rows.Count > 65000) { rows.Clear(); return; }
        // Native swap-with-last increments the index even after removal, skipping the moved row.
        for (var i = 0; i < rows.Count; i++)
            // 0x462F49 compares the first three words to a zero-initialized temporary vector header.
            // The supplied record and the fourth coordinate are not part of that comparison.
            if (rows[i].FirstX == 0 && rows[i].FirstY == 0 && rows[i].LastX == 0)
            { rows[i] = rows[^1]; rows.RemoveAt(rows.Count-1); }
    }
    internal void Apply(GoldWorldBankSnapshot bank)
    {
        if (bank.Opcode != GoldWorldBankLayout.Bank1Opcode) return;
        Clear();
        foreach (var group in bank.Sections[5].Records)
        {
            // Native AVL insertion assigns a new vector when the bank repeats a key: last wins.
            var rows = Enumerable.Range(0, group.Count).Select(i => GoldScriptRegionRecord.FromPayload(group.GetElement(i))).ToList();
            _records[group.Key] = rows; _views[group.Key] = rows.AsReadOnly();
        }
    }
    internal void Clear() { _records.Clear(); _views.Clear(); }
}
