using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class RegistryInsertionChecks
{
    private sealed class ObjectState(uint address, uint flags = 0)
    {
        public uint Address { get; } = address;
        public uint Flags { get; } = flags;
        public uint Reference { get; } = uint.MaxValue;
        public byte Byte38 { get; set; } = 0xa5;
    }

    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-registry-insertion.json")));
        var data = native.RootElement;
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var reference = row.GetProperty("reference").GetUInt32();
            var oldFlags = row.GetProperty("old_flags");
            var change = row.GetProperty("change").GetInt32();
            var old = new ObjectState(data.GetProperty("old").GetUInt32(), oldFlags.ValueKind == JsonValueKind.Null ? 0 : oldFlags.GetUInt32());
            var other = new ObjectState(data.GetProperty("other").GetUInt32(), change == 3 ? 0x10u : 0);
            var created = new ObjectState(data.GetProperty("new").GetUInt32());
            var slots = Enumerable.Repeat<ObjectState?>(null, row.GetProperty("count").GetInt32()).ToList();
            if (oldFlags.ValueKind != JsonValueKind.Null && reference < slots.Count) slots[(int)reference] = old;
            var counter = new WorldObjectAllocationCounter(row.GetProperty("previous").GetByte());
            var events = new List<uint[]>();
            var result = WorldObjectRegistryInsertion.AllocateExplicit(reference, slots, Array.Empty<uint>(), counter,
                new WorldObjectRegistryInsertionCallbacks<ObjectState>(reserved =>
                    {
                        Check(reserved == reference, "constructor receives requested slot");
                        events.Add([slots[(int)reference]?.Address ?? 0, (uint)slots.Count * 4, counter.Value]);
                        if (change == 1) slots[(int)reference] = null;
                        else if (change is 2 or 3) slots[(int)reference] = other;
                        for (var i = 0; i < row.GetProperty("nested").GetInt32(); i++) counter.BeginAllocation();
                        return row.GetProperty("failed").GetBoolean() ? null : created;
                    }, value => value.Flags, value =>
                    {
                        events.Add([value.Address, slots[(int)reference]?.Address ?? 0]);
                        if (row.GetProperty("dtor_replace").GetBoolean())
                        {
                            slots[(int)reference] = other;
                            for (var i = 0; i < 3; i++) counter.BeginAllocation();
                        }
                    }, (value, allocationByte) => value.Byte38 = allocationByte,
                    value => events.Add([slots[(int)reference]?.Address ?? 0, value.Byte38])));
            Check((result is null ? 0 : reference) == row.GetProperty("result").GetUInt32(), "native return reference");
            Check(counter.Value == row.GetProperty("counter").GetByte(), "counter after constructor/destructor reentry");
            Check(created.Byte38 == row.GetProperty("new_byte38").GetByte(), "late allocation-byte assignment");
            Check(slots.Count == row.GetProperty("count_after").GetInt32(), "logical growth retained even on failure");
            var expectedSlots = row.GetProperty("slots").EnumerateArray().Select(value => value.GetUInt32()).ToArray();
            Check(slots.Select(value => value?.Address ?? 0).SequenceEqual(expectedSlots.Take(slots.Count)), "native logical slots including holes");
            Check(old.Reference == row.GetProperty("old_reference").GetUInt32() && other.Reference == row.GetProperty("other_reference").GetUInt32(), "no stored-reference repair");
            var expected = row.GetProperty("events").EnumerateArray().ToArray();
            Check(events.Count == expected.Length, "constructor/destructor/activation event count");
            for (var i = 0; i < events.Count; i++)
                Check(events[i].SequenceEqual(expected[i].EnumerateArray().Skip(1).Select(value => value.GetUInt32())), "ordered native callback inputs");
        }
        // A nonempty read-only source must fail before consuming counter or growing slots.
        var guardedCounter = new WorldObjectAllocationCounter(255);
        var guardedSlots = new List<ObjectState?>();
        var callbacks = new WorldObjectRegistryInsertionCallbacks<ObjectState>(_ => throw new Exception("Guarded constructor ran"),
            _ => 0, _ => { }, (_, _) => { }, _ => { });
        try
        {
            WorldObjectRegistryInsertion.AllocateExplicit(17, guardedSlots, new uint[] { 17 }, guardedCounter, callbacks);
            throw new Exception("Read-only pending-free source was accepted");
        }
        catch (NotSupportedException) { Check(guardedCounter.Value == 255 && guardedSlots.Count == 0, "read-only source fails before effects"); }
        return checks;
        void Check(bool pass, string why)
        {
            if (!pass) throw new Exception("Registry insertion: " + why);
            checks++;
        }
    }
}
