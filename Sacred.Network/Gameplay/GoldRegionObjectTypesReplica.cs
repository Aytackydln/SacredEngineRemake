using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 4ABFE0's fifty 172-byte region replacement records. World/model consequences remain ordered work.</summary>
public sealed record GoldRegionObjectTypesWork(GoldRegionObjectTypesEvent Event, IReadOnlyList<GoldReplicatedObject> ReceiptObjects, uint? ReceiptGenerationWord);

public sealed class GoldRegionObjectTypesReplica
{
    private readonly Dictionary<int, ReadOnlyMemory<byte>> _records = [];
    private readonly List<GoldRegionObjectTypesWork> _pending = [];
    private readonly List<GoldRegionObjectTypesWork> _waiting = [];
    public GoldRegionObjectTypesReplica()
    {
        Records = new ReadOnlyDictionary<int, ReadOnlyMemory<byte>>(_records);
        Pending = _pending.AsReadOnly(); WaitingForBaseline = _waiting.AsReadOnly();
    }
    public IReadOnlyDictionary<int, ReadOnlyMemory<byte>> Records { get; }
    public IReadOnlyList<GoldRegionObjectTypesWork> Pending { get; }
    public IReadOnlyList<GoldRegionObjectTypesWork> WaitingForBaseline { get; }
    /// <summary>Native global 94E07C; absent from this packet. Supply the real world baseline, never infer it from receipt.</summary>
    public uint? GenerationWord { get; private set; }
    public long UnsafeAppendCount { get; private set; }
    public long IgnoredRegionCount { get; private set; }
    public void ConfigureGeneration(uint generation)
    {
        GenerationWord = generation;
        RetryWaiting();
    }
    /// <summary>Seed a known native table row before receipt; the native empty-table baseline is all zero.</summary>
    public void Seed(int region, ReadOnlySpan<byte> record)
    {
        if (region is < 0 or >= 50 || record.Length != 172 || BinaryPrimitives.ReadInt32LittleEndian(record[8..]) > 20)
            throw new ArgumentException("Invalid Gold region record baseline.");
        _records[region] = record.ToArray(); RetryWaiting();
    }
    internal bool Apply(GoldRegionObjectTypesEvent entry, IReadOnlyDictionary<uint, GoldReplicatedObject> objects)
    {
        if (entry.RegionIndex is < 0 or >= 50) { IgnoredRegionCount++; return false; }
        var work = new GoldRegionObjectTypesWork(entry, Array.AsReadOnly(objects.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray()), GenerationWord);
        if (GenerationWord is null) _waiting.Add(work); else ApplyKnown(work);
        return true;
    }
    private void RetryWaiting()
    {
        var waiting = _waiting.ToArray(); _waiting.Clear();
        foreach (var work in waiting)
            if (GenerationWord is null) _waiting.Add(work); else ApplyKnown(work with { ReceiptGenerationWord = work.ReceiptGenerationWord ?? GenerationWord });
    }
    private void ApplyKnown(GoldRegionObjectTypesWork work)
    {
        var entry = work.Event;
        if (_waiting.Any(previous => previous.Event.RegionIndex == entry.RegionIndex)) { _waiting.Add(work); return; }
        if ((work.ReceiptGenerationWord == entry.GenerationWord || work.ReceiptGenerationWord == uint.MaxValue) && !_records.ContainsKey(entry.RegionIndex))
        { _waiting.Add(work); return; }
        var record = entry.Record.ToArray(); var count = entry.EntryCount;
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(8), count);
        var mask = BinaryPrimitives.ReadUInt32LittleEndian(record) << 16;
        for (var i = 0; i < count; i++)
        {
            var word = record.AsSpan(16 + i * 8);
            BinaryPrimitives.WriteUInt32LittleEndian(word, BinaryPrimitives.ReadUInt32LittleEndian(word) | mask);
        }
        if ((work.ReceiptGenerationWord == entry.GenerationWord || work.ReceiptGenerationWord == uint.MaxValue) &&
            _records.TryGetValue(entry.RegionIndex, out var current) && BinaryPrimitives.ReadInt32LittleEndian(current.Span[8..]) is > 0 and var oldCount)
        {
            if (oldCount + Math.Max(count, 0) > 20) { UnsafeAppendCount++; return; }
            var merged = current.ToArray();
            for (var i = 0; i < count; i++) record.AsSpan(12 + i * 8, 8).CopyTo(merged.AsSpan(12 + (oldCount + i) * 8));
            BinaryPrimitives.WriteInt32LittleEndian(merged.AsSpan(8), oldCount + count); record = merged;
        }
        _records[entry.RegionIndex] = record;
        // This immutable work retains wire operands; execution must resolve/transform eligible world objects separately.
        _pending.Add(work);
    }
    internal void Clear() { _records.Clear(); _pending.Clear(); _waiting.Clear(); GenerationWord = null; IgnoredRegionCount = UnsafeAppendCount = 0; }
}
