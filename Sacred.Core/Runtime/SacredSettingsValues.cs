namespace Sacred.Core.Runtime;

/// <summary>Logical byte-key/value content of the original settings map. Reads
/// copy the first equal entry and do not insert missing keys. Original loading
/// can insert duplicates; ordinary writes and removal affect only the first.
/// File parsing and default decisions belong to their original callers.</summary>
public sealed class SacredSettingsValues
{
    private readonly SortedDictionary<ReadOnlyMemory<byte>, List<byte[]>> _values = new(SacredSettingsKeyComparer.Instance);
    public int Count { get; private set; }
    public byte[] Read(ReadOnlyMemory<byte> key) => _values.TryGetValue(key, out var values) ? values[0].ToArray() : [];
    public void Write(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value)
    {
        if (_values.TryGetValue(key, out var values)) values[0] = value.ToArray();
        else Insert(key, value);
    }
    public void Insert(ReadOnlyMemory<byte> key, ReadOnlyMemory<byte> value)
    {
        if (!_values.TryGetValue(key, out var values)) _values.Add(key.ToArray(), values = []);
        values.Add(value.ToArray()); Count++;
    }
    public bool Remove(ReadOnlyMemory<byte> key)
    {
        if (!_values.TryGetValue(key, out var values)) return false;
        values.RemoveAt(0); Count--;
        if (values.Count == 0) _values.Remove(key);
        return true;
    }
    public void Clear() { _values.Clear(); Count = 0; }
    public IReadOnlyList<KeyValuePair<byte[], byte[]>> Snapshot() => _values
        .SelectMany(pair => pair.Value.Select(value => new KeyValuePair<byte[], byte[]>(pair.Key.ToArray(), value.ToArray()))).ToArray();
    /// <summary>Owned per-entry copies read as iteration advances. Updates to
    /// later values remain visible; structural key changes invalidate traversal.</summary>
    public IEnumerable<KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> EnumerateEntries()
    {
        foreach (var pair in _values)
            for (var index = 0; index < pair.Value.Count; index++)
                yield return new(pair.Key.ToArray(), pair.Value[index].ToArray());
    }
}
