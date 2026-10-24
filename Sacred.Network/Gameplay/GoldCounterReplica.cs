using System.Buffers.Binary;
using System.Collections.Frozen;
using Sacred.Core.Pak.Items;

namespace Sacred.Network.Gameplay;

/// <summary>Gold network-mode kill/collection counters. Offline deletion and UI paths differ and are not substituted.</summary>
public sealed class GoldCounterReplica
{
    private readonly List<GoldCounterRecord> _kills = [], _collected = [];
    private readonly List<GoldCounterMapping> _mappings = [];
    private readonly List<GoldCounterCallback> _callbacks = [];
    private readonly List<GoldCounterCollectionAttempt> _collections = [];
    private readonly Queue<(GoldCounterEvent Event, GoldScriptFunctionSnapshot Functions)> _pending = [];
    private FrozenDictionary<uint, SacredItemCategory>? _categories;
    private bool _killsKnown, _collectedKnown, _mappingsKnown;
    public GoldCounterReplica()
    {
        Kills = _kills.AsReadOnly(); Collected = _collected.AsReadOnly(); Mappings = _mappings.AsReadOnly();
        Callbacks = _callbacks.AsReadOnly(); CollectionAttempts = _collections.AsReadOnly();
    }
    public IReadOnlyList<GoldCounterRecord> Kills { get; }
    public IReadOnlyList<GoldCounterRecord> Collected { get; }
    public IReadOnlyList<GoldCounterMapping> Mappings { get; }
    public IReadOnlyList<GoldCounterCallback> Callbacks { get; }
    public IReadOnlyList<GoldCounterCollectionAttempt> CollectionAttempts { get; }
    public GoldCounterMessages Messages { get; } = new();
    public int PendingCount => _pending.Count;
    public long ReceivedCount { get; private set; }
    /// <summary>Authored/runtime baseline supplied explicitly; empty network bank sections do not clear native tables.</summary>
    public void ConfigureBaseline(IEnumerable<GoldCounterRecord> kills, IEnumerable<GoldCounterRecord> collected, IEnumerable<GoldCounterMapping> mappings)
    {
        if (!_killsKnown) { _kills.AddRange(kills); _killsKnown = true; }
        if (!_collectedKnown) { _collected.AddRange(collected); _collectedKnown = true; }
        if (!_mappingsKnown) { _mappings.AddRange(mappings); _mappingsKnown = true; }
        Drain();
    }
    internal void ConfigureCategories(IReadOnlyDictionary<uint, SacredItemCategory> categories) { _categories = categories.ToFrozenDictionary(); Drain(); }
    internal void Apply(GoldCounterEvent incoming, GoldScriptFunctionReplica functions,
        IReadOnlyDictionary<uint, GoldReplicatedObject> objects, byte slot)
    {
        var entry = GoldCounterCodec.Decode(incoming.Message); ReceivedCount++;
        if (entry is GoldCounterMessageEvent message)
        { Messages.Add(new(message.Mode, message.Flags, message.TextResource)); return; }
        if (entry is GoldCounterCollectEvent collect)
            _collections.Add(new(collect, objects.GetValueOrDefault(collect.ObjectReference), objects.GetValueOrDefault(slot), slot));
        var work = (entry, functions.Capture());
        if (_pending.Count != 0 || !TryApply(work)) _pending.Enqueue(work);
        Drain();
    }
    private bool TryApply((GoldCounterEvent Event, GoldScriptFunctionSnapshot Functions) work)
    {
        if (work.Event is GoldCounterTargetEvent target)
        {
            var collectNotice = target.Message.Opcode == GoldCounterLayout.CollectNoticeOpcode;
            if (collectNotice ? !_collectedKnown : !_killsKnown) return false;
            var records = collectNotice ? _collected : _kills;
            if (target.Message.Opcode == GoldCounterLayout.KillOpcode) Decrement(records, target.TargetResource, GoldCounterLayout.KillCompleteText, work.Functions);
            else foreach (var row in records.Where(r => r.TargetResource == target.TargetResource))
                Messages.Add(new(0x10, 0x80000003, null, target.TargetResource, unchecked(row.Initial - row.Remaining), row.Initial));
        }
        else if (work.Event is GoldCounterCollectEvent collect)
        {
            if (!_collectedKnown) return false;
            bool NeedsClassification(GoldCounterRecord row) => row.TargetResource > 0 && row.TargetResource < GoldCounterLayout.ResourceLimit;
            if (_categories is null && _collected.Any(NeedsClassification)) return false;
            bool IsRemappable(GoldCounterRecord row) => NeedsClassification(row) && (byte)_categories!.GetValueOrDefault(row.TargetResource) is 2 or 3 or 32;
            if (!_mappingsKnown && _collected.Any(IsRemappable)) return false;
            // The remapping pass precedes the collection attempt/counter-match pass even for opcode 1A2.
            for (var i = 0; i < _collected.Count; i++)
            {
                var resource = _collected[i].TargetResource;
                if (!IsRemappable(_collected[i])) continue;
                var match = _mappings.FindIndex(r => r.Source == resource);
                if (match >= 0) _collected[i] = _collected[i].WithTarget(_mappings[match].Replacement);
            }
            if (collect.UpdatesCounter) Decrement(_collected, collect.TargetResource, GoldCounterLayout.CollectCompleteText, work.Functions);
        }
        return true;
    }
    private void Decrement(List<GoldCounterRecord> records, uint resource, uint completeText, GoldScriptFunctionSnapshot functions)
    {
        for (var i = 0; i < records.Count; i++)
        {
            if (records[i].TargetResource != resource) continue;
            var changed = records[i].Decrement(); records[i] = changed;
            if (changed.Remaining != 0 && changed.Remaining <= GoldCounterLayout.MaximumRemaining) continue;
            Messages.Add(new(0x10, 3, completeText)); _callbacks.Add(new(changed, functions));
            // Network mode retains the row and exits on the first completion; it does not swap-remove it.
            break;
        }
    }
    private void Drain() { while (_pending.TryPeek(out var work) && TryApply(work)) _pending.Dequeue(); }
    internal void Apply(GoldWorldBankSnapshot bank)
    {
        if (bank.Opcode != GoldWorldBankLayout.Bank1Opcode) return;
        void Replace(int section, List<GoldCounterRecord> target, ref bool known)
        {
            if (bank.Sections[section].Count == 0) return;
            target.Clear(); target.AddRange(bank.Sections[section].Records.Select(r => GoldCounterRecord.FromPayload(r.Data))); known = true;
        }
        Replace(GoldCounterLayout.KillBankSection, _kills, ref _killsKnown);
        Replace(GoldCounterLayout.CollectBankSection, _collected, ref _collectedKnown);
        if (bank.Sections[GoldCounterLayout.MappingBankSection].Count != 0)
        {
            _mappings.Clear();
            foreach (var row in bank.Sections[GoldCounterLayout.MappingBankSection].Records)
                _mappings.Add(new(BinaryPrimitives.ReadUInt32LittleEndian(row.Data), BinaryPrimitives.ReadUInt32LittleEndian(row.Data[4..]), BinaryPrimitives.ReadUInt32LittleEndian(row.Data[8..])));
            _mappingsKnown = true;
        }
        Drain();
    }
    internal void Clear()
    {
        _kills.Clear(); _collected.Clear(); _mappings.Clear(); _callbacks.Clear(); _collections.Clear(); _pending.Clear();
        _killsKnown = _collectedKnown = _mappingsKnown = false; ReceivedCount = 0; Messages.Clear();
    }
}
