using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class ReferenceSelectionChecks
{
    private sealed class ObjectState(uint address, uint flags, byte allocationByte)
    {
        public uint Address { get; } = address;
        public uint Flags { get; } = flags;
        public byte AllocationByte { get; } = allocationByte;
        public uint Reference { get; set; } = 17;
    }

    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-reference-selection.json")));
        var data = native.RootElement;
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var scenario = row.GetProperty("scenario").GetString();
            var count = row.GetProperty("count").GetInt32();
            var actor = new ObjectState(data.GetProperty("actor").GetUInt32(), scenario == "live" ? 0 : 0x10u, row.GetProperty("old_byte").GetByte());
            var slots = Enumerable.Repeat<ObjectState?>(null, count).ToList();
            if (count > 17 && scenario != "empty") slots[17] = actor;
            var available = row.GetProperty("pending").EnumerateArray().Select(value => value.GetUInt32()).ToList();
            var delayed = new List<WorldObjectReferenceDelay>();
            var cleanup = new List<uint>();
            var destroyed = new List<uint>();
            var queries = new List<(string, uint)>();
            var diagnostics = 0;
            var selected = WorldObjectReferenceSelector.Select(row.GetProperty("multiplayer").GetBoolean(), slots,
                available, delayed, new WorldObjectReferenceSelectionCallbacks<ObjectState>(value => value.Flags,
                    value => value.AllocationByte, reference => Query("local", reference), reference => Query("global", reference),
                    value =>
                    {
                        Check(slots[17] is null, "clear before destructor");
                        destroyed.Add(value.Address);
                        if (row.GetProperty("reentry").GetBoolean()) { available.Clear(); available.AddRange([99, 100]); }
                    }, () => data.GetProperty("clock").GetUInt32(), reference =>
                    {
                        if (reference != 0 && delayed.Count != 0)
                            Check(delayed[^1].Reference == reference, "selected delayed pair precedes final cleanup");
                        cleanup.Add(reference);
                    }, (_, _) => diagnostics++), row.GetProperty("has_output").GetBoolean() ? (byte)0xa5 : null);
            Check(selected.Reference == row.GetProperty("result").GetUInt32(), $"original selected reference: managed {selected}; native {row}");
            Check((selected.AllocationByte ?? 0xa5) == row.GetProperty("output").GetByte(), "allocation-byte write/unchanged/absent pointer");
            Check(slots.Count == row.GetProperty("count_after").GetInt32(), "logical growth and offline refill");
            Check(available.SequenceEqual(UInts(row, "pending_after")), "queue after rejection/refill/destructor reentry/pop");
            var slot17 = row.GetProperty("slot17");
            Check((slots.Count > 17 ? slots[17]?.Address ?? 0 : (uint?)null) == (slot17.ValueKind == JsonValueKind.Null ? null : slot17.GetUInt32()), "occupied/marked slot state");
            Check(cleanup.SequenceEqual(UInts(row, "cleanup")), "ordered refill/final cleanup calls including zero");
            Check(destroyed.SequenceEqual(UInts(row, "destroyed")), "marked object destruction");
            Check(queries.SequenceEqual(row.GetProperty("queries").EnumerateArray().Select(value => (value[0].GetString()!, value[1].GetUInt32()))), "usage query order and short circuit");
            Check(diagnostics == row.GetProperty("diagnostic_count").GetInt32(), "native rejected-entry diagnostic count");
            Check(delayed.SequenceEqual(row.GetProperty("records").EnumerateArray().Select(value => new WorldObjectReferenceDelay(value[0].GetUInt32(), value[1].GetUInt32()))), "selected delayed records and clock arithmetic");
            bool Query(string kind, uint reference) { queries.Add((kind, reference)); return reference == 17 && scenario == kind; }
        }
        return checks + RunComposed(root);
        void Check(bool pass, string why)
        {
            if (!pass) throw new Exception("Reference selection: " + why);
            checks++;
        }
    }
    private static IEnumerable<uint> UInts(JsonElement row, string name) => row.GetProperty(name).EnumerateArray().Select(value => value.GetUInt32());

    private static int RunComposed(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-selection-cleanup.json")));
        var data = native.RootElement;
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var actor = new ObjectState(data.GetProperty("actor").GetUInt32(), 0x10, 255);
            var slots = Enumerable.Repeat<ObjectState?>(null, 32).ToList();
            if (row.GetProperty("scenario").GetString() == "free") slots[17] = actor;
            var available = UInts(row, "pending").ToList();
            var delayed = new List<WorldObjectReferenceDelay>();
            var cleanup = new List<uint>();
            var destroyed = new List<uint>();
            var queries = new List<(string, uint)>();
            var selected = WorldObjectReferenceSelector.Select(true, slots, available, delayed,
                new WorldObjectReferenceSelectionCallbacks<ObjectState>(value => value.Flags, value => value.AllocationByte,
                    reference => Query("local", reference), reference => Query("global", reference), value => destroyed.Add(value.Address),
                    () => data.GetProperty("clock").GetUInt32(), reference =>
                    {
                        cleanup.Add(reference);
                        WorldObjectReferenceCleanup.Clean(reference, false, slots, available,
                            new WorldObjectReferenceCleanupCallbacks<ObjectState>(value => value.Reference,
                                (value, next) => value.Reference = next, value => value.Flags, null, _ => { }));
                    }, (_, _) => { }), 0xa5);
            Check(selected.Reference == row.GetProperty("result").GetUInt32(), "selected reference");
            Check(selected.AllocationByte == row.GetProperty("output").GetByte(), "output wrap/unchanged");
            Check(available.SequenceEqual(UInts(row, "pending_after")), "selection followed by actual duplicate cleanup");
            Check((slots[17]?.Address ?? 0) == row.GetProperty("slot17").GetUInt32(), "marked destruction slot state");
            Check(cleanup.SequenceEqual(UInts(row, "cleanup")), "composed cleanup calls");
            Check(destroyed.SequenceEqual(UInts(row, "destroyed")), "composed destruction");
            Check(queries.SequenceEqual(row.GetProperty("queries").EnumerateArray().Select(value => (value[0].GetString()!, value[1].GetUInt32()))), "composed membership boundaries");
            Check(delayed.SequenceEqual(row.GetProperty("records").EnumerateArray().Select(value => new WorldObjectReferenceDelay(value[0].GetUInt32(), value[1].GetUInt32()))), "composed delayed pairs");
            bool Query(string kind, uint reference) { queries.Add((kind, reference)); return false; }
        }
        return checks;
        void Check(bool pass, string why)
        {
            if (!pass) throw new Exception("Composed selection/cleanup: " + why);
            checks++;
        }
    }
}
