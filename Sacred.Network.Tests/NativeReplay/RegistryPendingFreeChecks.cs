using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class RegistryPendingFreeChecks
{
    private sealed class ObjectState(uint address, uint flags = 0)
    {
        public uint Address { get; } = address;
        public uint Flags { get; } = flags;
        public byte Byte38 { get; set; } = 0xa5;
    }

    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-registry-pending-free.json")));
        var data = native.RootElement;
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var pending = row.GetProperty("pending").EnumerateArray().Select(value => value.GetUInt32()).ToList();
            var oldFlags = row.GetProperty("old_flags");
            var old = new ObjectState(data.GetProperty("old").GetUInt32(), oldFlags.ValueKind == JsonValueKind.Null ? 0 : oldFlags.GetUInt32());
            var created = new ObjectState(data.GetProperty("new").GetUInt32());
            var slots = Enumerable.Repeat<ObjectState?>(null, 32).ToList();
            if (oldFlags.ValueKind != JsonValueKind.Null) slots[17] = old;
            var counter = new WorldObjectAllocationCounter(row.GetProperty("previous").GetByte());
            var events = new List<uint[]>();
            var result = WorldObjectRegistryInsertion.AllocateExplicit(17, slots, pending, counter,
                new WorldObjectRegistryInsertionCallbacks<ObjectState>(_ =>
                {
                    ChangePending(1);
                    events.Add([slots[17]?.Address ?? 0, (uint)slots.Count * 4, counter.Value]);
                    return row.GetProperty("failed").GetBoolean() ? null : created;
                }, value => value.Flags, value =>
                {
                    ChangePending(2);
                    events.Add([value.Address, slots[17]?.Address ?? 0]);
                }, (value, allocationByte) => value.Byte38 = allocationByte,
                value => events.Add([slots[17]?.Address ?? 0, value.Byte38])));
            Check(pending.SequenceEqual(row.GetProperty("pending_after").EnumerateArray().Select(value => value.GetUInt32())),
                "ordered first-match erase after destructor, including duplicate/missing references and reentry");
            Check((result is null ? 0u : 17u) == row.GetProperty("result").GetUInt32(), "native allocation result");
            Check(counter.Value == row.GetProperty("counter").GetByte() && created.Byte38 == row.GetProperty("new_byte38").GetByte(), "native counter and prepare ordering");
            Check((slots[17]?.Address ?? 0) == row.GetProperty("slot").GetUInt32(), "slot after native insertion/failure");
            var expected = row.GetProperty("events").EnumerateArray().ToArray();
            Check(events.Count == expected.Length, "constructor/destructor/activation count");
            for (var i = 0; i < events.Count; i++)
                Check(events[i].SequenceEqual(expected[i].EnumerateArray().Skip(1).Select(value => value.GetUInt32())), "ordered native callback inputs");

            void ChangePending(int stage)
            {
                if (row.GetProperty("reentry").GetInt32() != stage) return;
                pending.Clear();
                pending.AddRange([7, 17, 8, 17]);
            }
        }
        return checks;
        void Check(bool pass, string why)
        {
            if (!pass) throw new Exception("Registry pending-free: " + why);
            checks++;
        }
    }
}
