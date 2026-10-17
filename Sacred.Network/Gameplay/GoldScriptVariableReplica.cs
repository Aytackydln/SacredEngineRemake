using System.Collections.ObjectModel;
using System.Text;

namespace Sacred.Network.Gameplay;

/// <summary>Native indexed variable vector. Slots grown without received bytes remain explicitly unknown.</summary>
public sealed class GoldScriptVariableReplica
{
    private readonly Dictionary<uint, GoldScriptVariableRecord> _records = [];
    public GoldScriptVariableReplica() => Records = new ReadOnlyDictionary<uint, GoldScriptVariableRecord>(_records);
    public IReadOnlyDictionary<uint, GoldScriptVariableRecord> Records { get; }
    public uint Count { get; private set; }
    public uint UnknownSlotCount => Count - (uint)_records.Count;
    public long AppliedUpdateCount { get; private set; }
    /// <summary>Search received records in native index order. Unknown slots may hide an earlier match;
    /// a script executor must account for UnknownSlotCount before claiming a complete lookup.</summary>
    public bool TryGetValue(string name, out int value)
    {
        var bytes = Encoding.Latin1.GetBytes(name);
        foreach (var pair in _records.OrderBy(pair => pair.Key))
            if (pair.Value.MatchesName(bytes)) { value = pair.Value.Value; return true; }
        value = default;
        return false;
    }
    internal void Apply(GoldWorldBankSnapshot bank)
    {
        if (bank.Opcode != GoldScriptVariableLayout.BankOpcode) return;
        var section = bank.Sections[GoldScriptVariableLayout.BankSection];
        var values = section.Records.Select(row => GoldScriptVariableRecord.FromPayload(row.Data)).ToArray();
        _records.Clear(); Count = (uint)values.Length;
        for (uint index = 0; index < Count; index++) _records[index] = values[index];
    }
    internal void Apply(GoldScriptVariableEvent update)
    {
        var index = update.Index ?? Count;
        if (index >= Count)
        {
            var count = update.Index.HasValue ? (ulong)index + GoldScriptVariableLayout.GrowthSlots : (ulong)Count + 1;
            // Native vector byte-size arithmetic cannot represent a larger allocation.
            if (count * GoldScriptVariableLayout.RecordSize > uint.MaxValue)
                throw new InvalidDataException("Script variable index overflows the native vector allocation.");
            Count = (uint)count;
        }
        _records[index] = update.Variable;
        AppliedUpdateCount++;
    }
    internal void Clear() { _records.Clear(); Count = 0; AppliedUpdateCount = 0; }
}
