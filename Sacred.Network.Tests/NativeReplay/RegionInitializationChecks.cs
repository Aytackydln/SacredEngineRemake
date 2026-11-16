using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class RegionInitializationChecks
{
    public static byte[] WireExample(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-region-initialization.json")));
        var publication = native.RootElement.GetProperty("cases").EnumerateArray()
            .Where(row => row.GetProperty("spec").GetProperty("raw").GetByte() == 1)
            .SelectMany(row => row.GetProperty("events").EnumerateArray())
            .First(row => row[0].GetString() == "publish");
        var bytes = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, publication[2].GetUInt32());
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), publication[3].GetUInt32());
        return bytes;
    }
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-region-initialization.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var spec = row.GetProperty("spec"); var mapped = row.GetProperty("mapped").GetUInt16();
            var initial = new Dictionary<uint, (uint Value, uint Tag)>();
            if (spec.GetProperty("present").GetBoolean()) initial[mapped] = (123, 0xa5000007);
            var state = new WorldScriptRegionInitialization(initial); var flags = spec.GetProperty("flags").GetUInt32();
            var events = new List<object[]>();
            var services = new WorldScriptRegionInitializationServices(sector => { events.Add(["sector", sector]); return spec.GetProperty("raw").GetByte(); },
                _ => mapped, _ => flags, (raw, mask) =>
                {
                    events.Add(["flags", raw, mask, 1]); flags |= mask;
                    if (spec.GetProperty("multiplayer").GetBoolean()) events.Add(["publish", 0x185, raw, mask]);
                },
                _ => spec.GetProperty("cached").ValueKind == JsonValueKind.Null ? null : spec.GetProperty("cached").GetUInt32(),
                _ => spec.GetProperty("match").GetString() switch { "zero" => 0, "one" => 1, _ => -1 },
                index =>
                {
                    events.Add(["invoke", index, state.CurrentRegion, flags]);
                    if (spec.GetProperty("mutate_current").GetBoolean()) state.CurrentRegion = 997;
                    return true;
                });
            var result = state.TryInitialize(77, true, spec.GetProperty("multiplayer").GetBoolean(), services);
            foreach (var pair in state.Records)
                if (!initial.TryGetValue(pair.Key, out var previous) || previous != pair.Value)
                    events.Add(["insert", pair.Key, pair.Value.Value, pair.Value.Tag]);
            Check(result == row.GetProperty("result").GetBoolean(), "native return");
            Check(flags == row.GetProperty("flags").GetUInt32() && state.CurrentRegion == row.GetProperty("current").GetUInt32(), "flags/current-region timing");
            Check(events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), $"exact ordered side effects {spec}; expected {row.GetProperty("events")}; actual {JsonSerializer.Serialize(events)}");
            Check(state.Records.OrderBy(pair => pair.Key).Select(pair => JsonSerializer.Serialize(new uint[] { pair.Key, pair.Value.Value, pair.Value.Tag }))
                .SequenceEqual(row.GetProperty("records").EnumerateArray().Select(value => value.GetRawText())), "separate lifecycle state");
        }
        foreach (var row in native.RootElement.GetProperty("invalid").EnumerateArray())
        {
            var state = new WorldScriptRegionInitialization { CurrentRegion = 0x12345678 };
            var services = new WorldScriptRegionInitializationServices(_ => throw new Exception("Unexpected lookup"), _ => 0, _ => 0, (_, _) => { }, _ => null, _ => -1, _ => true);
            Check(state.TryInitialize(row.GetProperty("sector").GetUInt32(), row.GetProperty("world").GetUInt32() != 0, true, services) == row.GetProperty("result").GetBoolean() &&
                state.CurrentRegion == row.GetProperty("current").GetUInt32(), "null sector/world skips lookup and preserves current state");
        }
        return checks;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Region initialization: " + why); checks++; }
    }
}
