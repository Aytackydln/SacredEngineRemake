using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Quest header, entry, database and journal deltas. Script execution and quest UI are separate consumers.</summary>
public sealed class GoldQuestReplica
{
    private readonly Queue<GoldQuestEvent> _pending = [];
    public GoldIndexedScriptTable<GoldQuestEntryState> Entries { get; } = new();
    public GoldIndexedScriptTable<GoldWorldBankRecord> Headers { get; } = new();
    public GoldIndexedScriptTable<GoldWorldBankRecord> Database { get; } = new();
    /// <summary>368 serialized bytes per record; native runtime +0x0C is deliberately absent.</summary>
    public GoldIndexedScriptTable<GoldWorldBankRecord> Journal { get; } = new();
    public GoldQuestJournalRange JournalRange { get; } = new();
    public int PendingCount => _pending.Count + JournalRange.PendingCount;
    public long ReceivedCount { get; private set; }
    public long MissingCreatureCount { get; private set; }

    internal void Apply(GoldQuestEvent entry, GoldWorldReplica world, Dictionary<uint, GoldReplicatedObject> objects)
    {
        ReceivedCount++;
        switch (GoldQuestCodec.Decode(entry.Message))
        {
            case GoldQuestProgressEvent progress:
                ApplyOrEnqueue(progress);
                break;
            case GoldQuestInsertEvent insertion:
                if (!objects.TryGetValue(insertion.ObjectReference, out var actor) || actor.Creature is null ||
                    (actor.BaseState.Flags & GoldObjectLifecycleLayout.InactiveFlag) != 0)
                { MissingCreatureCount++; break; }
                var index = Database.Count;
                GoldQuestLayout.RequireIndex(index, 1, GoldQuestLayout.DatabaseRuntimeStride);
                // 0x450C50 accepts the signed low word, including negative wrapped indices.
                var tag = GoldObjectQuestCodec.Decode(new(GoldObjectQuestLayout.NumberOpcode, 0, 0, 0,
                    [..BitConverter.GetBytes(insertion.ObjectReference), ..BitConverter.GetBytes(index)]));
                GoldObjectQuestReducer.Apply(objects, tag);
                actor = objects[insertion.ObjectReference];
                var bytes = actor.Creature!.ToPayload();
                var offset = GoldCreatureStateLayout.Word200Offset;
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset),
                    BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) | GoldQuestLayout.CreatureQuestInsertedFlag);
                objects[insertion.ObjectReference] = actor with { Creature = GoldCreatureState.FromPayload(bytes) };
                Database.Set(index, insertion.Record, 1);
                break;
            case GoldQuestUpdateEvent update:
                switch (update.Message.Opcode)
                {
                    case GoldQuestLayout.EntryOpcode:
                        Entries.Set(update.Index, new(update.Record.Data, world.WorldBanks.Functions.Capture()), GoldQuestLayout.GrowthSlots);
                        break;
                    case GoldQuestLayout.HeaderOpcode: Headers.Set(update.Index, update.Record, 1); break;
                    case GoldQuestLayout.JournalOpcode:
                        Journal.Set(update.Index, update.Record, 1);
                        if (BinaryPrimitives.ReadUInt32LittleEndian(update.Record.Data[4..]) is 100 or 101)
                            JournalRange.Apply(update.Index, Journal.Count);
                        break;
                    case GoldQuestLayout.DatabaseOpcode: Database.Set(update.Index, update.Record, GoldQuestLayout.GrowthSlots); break;
                    case GoldQuestLayout.DatabaseAndEntriesOpcode:
                        Database.Set(update.Index, update.Record, 1);
                        // Native 0x80730C/0x80735B selects the header using database byte +1,
                        // then overwrites that header's +0x64 vector from its start.
                        ApplyOrEnqueue(update);
                        break;
                }
                break;
        }
        DrainPending();
    }

    private bool ApplyProgress(GoldQuestProgressEvent entry)
    {
        if (entry.HeaderIndex < 0 || entry.Entries.Count == 0) return true;
        if (Headers.Find((uint)entry.HeaderIndex) is not { } header) return false;
        var words = GoldQuestRecordVectors.Words(header.Vectors[0]);
        if (entry.Entries.Any(index => index >= words.Length)) return false;
        var database = entry.DatabaseIndex == uint.MaxValue ? null : Database.Find(entry.DatabaseIndex);
        if (entry.DatabaseIndex != uint.MaxValue && database is null) return false;
        foreach (var index in entry.Entries) words[index] = unchecked(words[index] + GoldQuestLayout.ProgressIncrement);
        Headers.Set((uint)entry.HeaderIndex, GoldQuestRecordVectors.WithWords(header, 0, words), 1);
        if (database is not null)
            Database.Set(entry.DatabaseIndex, GoldQuestRecordVectors.WithWords(database, 1,
                [..GoldQuestRecordVectors.Words(database.Vectors[1]), ..entry.Entries]), 1);
        return true;
    }

    private bool ApplyHeaderEntries((byte Header, byte[] Words) work)
    {
        if (Headers.Find(work.Header) is not { } header) return false;
        var words = GoldQuestRecordVectors.Words(header.Vectors[0]);
        var count = work.Words.Length / 4;
        // Native resizes to the tail's word count +32, preserving the existing tail,
        // then copies at vector start. Newly grown word elements are initialized to zero.
        Array.Resize(ref words, checked(count + GoldQuestLayout.GrowthSlots));
        for (var i = 0; i < count; i++) words[i] = BinaryPrimitives.ReadUInt32LittleEndian(work.Words.AsSpan(i * 4));
        Headers.Set(work.Header, GoldQuestRecordVectors.WithWords(header, 0, words), 1);
        return true;
    }

    private void DrainPending()
    {
        while (_pending.TryPeek(out var work) && ApplyPending(work)) _pending.Dequeue();
    }

    private bool ApplyPending(GoldQuestEvent work) => work switch
    {
        GoldQuestProgressEvent progress => ApplyProgress(progress),
        GoldQuestUpdateEvent update => ApplyHeaderEntries((update.Record.Prefix[1], update.HeaderEntries.ToArray())),
        _ => throw new InvalidOperationException("Unexpected pending quest operation.")
    };
    private void ApplyOrEnqueue(GoldQuestEvent work)
    {
        if (_pending.Count != 0 || !ApplyPending(work)) _pending.Enqueue(work);
    }

    internal void Apply(GoldWorldBankSnapshot bank, GoldScriptFunctionReplica functions)
    {
        if (bank.Opcode == GoldWorldBankLayout.Bank1Opcode && bank.Sections[0].Count != 0)
            Journal.Replace(bank.Sections[0].Records);
        if (bank.Opcode == GoldWorldBankLayout.Bank3Opcode && bank.Sections[1].Count != 0)
        {
            var entries = bank.Sections[1].Records.Select((r, i) => bank.IsServerSnapshot
                ? Entries.Find((uint)i)?.WithRuntimeWord(BinaryPrimitives.ReadUInt32LittleEndian(r.Data)) ??
                    GoldQuestEntryState.RuntimeOnly(BinaryPrimitives.ReadUInt32LittleEndian(r.Data))
                : new GoldQuestEntryState(r.Data, functions.Capture())).ToArray();
            Entries.Replace(entries);
        }
        if (bank.Opcode == GoldWorldBankLayout.Bank4Opcode)
        {
            if (bank.Sections[1].Count != 0) Headers.Replace(bank.Sections[1].Records);
            if (bank.Sections[5].Count != 0) Database.Replace(bank.Sections[5].Records);
        }
        DrainPending();
    }
    internal void Clear()
    {
        Entries.Clear(); Headers.Clear(); Database.Clear(); Journal.Clear();
        JournalRange.Clear();
        _pending.Clear(); ReceivedCount = MissingCreatureCount = 0;
    }
}
