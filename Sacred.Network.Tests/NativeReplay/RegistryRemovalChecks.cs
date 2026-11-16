using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class RegistryRemovalChecks
{
    private sealed class ObjectState(uint address)
    {
        public uint Address { get; } = address;
        public uint Reference { get; } = uint.MaxValue;
    }

    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-registry-removal.json")));
        var data = native.RootElement;
        var checks = 0;
        foreach (var row in data.GetProperty("removals").EnumerateArray())
        {
            var actor = new ObjectState(data.GetProperty("actor").GetUInt32());
            var reference = row.GetProperty("reference").GetUInt32();
            var slots = Enumerable.Repeat(row.GetProperty("present").GetBoolean() ? actor : null, 128).ToArray();
            var logical = new ArraySegment<ObjectState?>(slots, 0, row.GetProperty("count").GetInt32());
            var events = new List<uint[]>();
            WorldObjectRegistryRemoval.DestroySlot(reference, logical, value =>
            {
                Check(slots[(int)reference] is null, "slot cleared before destructor reentry");
                events.Add([value.Address, 1]);
                if (row.GetProperty("replacement").GetBoolean())
                    slots[(int)reference] = new(actor.Address + 0x100);
            });
            Check(slots.Select(value => value?.Address ?? 0).SequenceEqual(
                row.GetProperty("slots").EnumerateArray().Select(value => value.GetUInt32())), "all logical/capacity slots and reentry replacement");
            Check(logical.Count * 4 == row.GetProperty("end").GetUInt32(), "logical length unchanged");
            Check(actor.Reference == row.GetProperty("stored_after").GetUInt32(), "destruction does not repair stored reference");
            var expected = row.GetProperty("events").EnumerateArray().ToArray();
            Check(events.Count == expected.Length, "destructor count independent of flags");
            for (var i = 0; i < events.Count; i++)
                Check(events[i].SequenceEqual(expected[i].EnumerateArray().Skip(1).Select(value => value.GetUInt32())), "destructor inputs");
        }
        foreach (var row in data.GetProperty("gates").EnumerateArray())
            Check(WorldObjectRegistryRemoval.ProtectsLocalReference(row.GetProperty("reference").GetUInt32(),
                row.GetProperty("local").GetUInt32(), row.GetProperty("multiplayer").GetBoolean(),
                row.GetProperty("flag1").GetBoolean(), row.GetProperty("connected").GetBoolean(),
                row.GetProperty("flag28").GetBoolean()) == row.GetProperty("protected").GetBoolean(),
                "original high-level protection gate, including zero local reference");
        return checks;
        void Check(bool pass, string why)
        {
            if (!pass) throw new Exception("Registry removal: " + why);
            checks++;
        }
    }
}
