using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class ActiveHeroLookupChecks
{
    private sealed class ObjectState(uint address, uint reference)
    {
        public uint Address { get; } = address;
        public uint Reference { get; set; } = reference;
    }

    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-active-hero-lookup.json")));
        var data = native.RootElement;
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var actor = new ObjectState(data.GetProperty("actor").GetUInt32(), row.GetProperty("stored").GetUInt32());
            var replacement = new ObjectState(data.GetProperty("replacement_actor").GetUInt32(), 0xbadcafe);
            var slots = Enumerable.Repeat(row.GetProperty("present").GetBoolean() ? actor : null,
                row.GetProperty("count").GetInt32()).ToArray();
            var reference = row.GetProperty("reference").GetUInt32();
            var events = new List<uint[]>();
            ObjectState? result;
            if (row.GetProperty("mode").GetString() == "hero")
            {
                events.Add([]); // Supplied singleton query precedes all branches.
                result = WorldActiveHeroLookup.Resolve<ObjectState, ObjectState>(reference, slots,
                    value => value.Reference, (value, next) => value.Reference = next, Repair,
                    value =>
                    {
                        events.Add([value?.Address ?? 0]);
                        return row.GetProperty("cast_success").GetBoolean() ? value : null;
                    });
            }
            else result = WorldActiveHeroLookup.ResolveObject(reference, slots,
                value => value.Reference, (value, next) => value.Reference = next, Repair);
            Check((result?.Address ?? 0) == row.GetProperty("result").GetUInt32(), "native lookup result");
            Check(actor.Reference == row.GetProperty("stored_after").GetUInt32(), "reference repair including failed cast");
            Check(replacement.Reference == row.GetProperty("replacement_stored_after").GetUInt32(), "replacement is not repaired again");
            var expected = row.GetProperty("events").EnumerateArray().ToArray();
            Check(events.Count == expected.Length, "native query/repair/cast event count");
            for (var i = 0; i < events.Count; i++)
                Check(events[i].SequenceEqual(expected[i].EnumerateArray().Skip(1).Select(value => value.GetUInt32())),
                    "native ordered events and arguments");

            void Repair(uint next, uint previous)
            {
                Check(actor.Reference == next, "reference written before diagnostic");
                events.Add([next, previous]);
                var changed = row.GetProperty("replacement");
                if (changed.ValueKind != JsonValueKind.Null)
                    slots[(int)next] = changed.GetBoolean() ? replacement : null;
            }
        }
        return checks;
        void Check(bool pass, string why)
        {
            if (!pass) throw new Exception("Active hero lookup: " + why);
            checks++;
        }
    }
}
