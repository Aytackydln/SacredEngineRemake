using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

/// <summary>Sparse native vector: grown slots without transmitted bytes remain unknown.</summary>
public sealed class GoldIndexedScriptTable<T> where T : class
{
    private readonly Dictionary<uint, T> _records = [];
    public GoldIndexedScriptTable() => Records = new ReadOnlyDictionary<uint, T>(_records);
    public IReadOnlyDictionary<uint, T> Records { get; }
    public uint Count { get; private set; }
    public uint UnknownSlotCount => Count - (uint)_records.Count;
    public T? Find(uint index) => _records.GetValueOrDefault(index);
    internal void Set(uint index, T value, int growth)
    {
        if (index >= Count) Count = checked(index + (uint)growth);
        _records[index] = value;
    }
    internal void Replace(IEnumerable<T> records)
    {
        Clear();
        foreach (var record in records) _records[Count++] = record;
    }
    internal void Clear() { _records.Clear(); Count = 0; }
}
