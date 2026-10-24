using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using Sacred.Core.GameRes;
using Sacred.Core.Pak.Items;
using Sacred.Network.Gameplay;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

internal static class CounterReceiveChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Counter receive: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-counter-receives.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var world = new GoldWorldReplica();
            world.ConfigureObjectCategories(new Dictionary<uint, SacredItemCategory> { [200] = (SacredItemCategory)row.GetProperty("category").GetByte() });
            world.ConfigureObjectResources(new Dictionary<uint, uint> { [17] = 10017, [18] = 10018, [200] = 10200 });
            var counters = world.WorldBanks.Counters;
            var resources = new Dictionary<uint, string> { [10017] = "Resource10017", [10018] = "Resource10018", [10200] = "Resource10200",
                [SacredResourceHash.Compute("COUNT_MIDDLE")] = "of" };
            counters.Messages.ConfigureResources(new GameResStore(resources.ToFrozenDictionary()));
            byte[][] Records(string name) => row.GetProperty(name).EnumerateArray().Select(r => Convert.FromHexString(r.GetString()!)).ToArray();
            var kills = Records("kills"); var collected = Records("collected"); var mappings = Records("mappings");
            if (kills.Length != 0)
                world.Apply(GoldGameEventDecoder.Decode(Bank(0x1b1, (7, kills), (8, collected), (9, mappings))));
            else counters.ConfigureBaseline([], [], mappings.Select(m => new GoldCounterMapping(BitConverter.ToUInt32(m), BitConverter.ToUInt32(m, 4), BitConverter.ToUInt32(m, 8))));
            var names = fixture.RootElement.GetProperty("functions").EnumerateArray().Select(n => n.GetString()!).ToArray();
            var functions = new byte[4 + names.Length * 84]; BitConverter.GetBytes(names.Length).CopyTo(functions, 0);
            for (var i = 0; i < names.Length; i++) Encoding.Latin1.GetBytes(names[i]).CopyTo(functions, 4 + i * 84);
            world.WorldBanks.Functions.Configure(functions);
            var opcode = row.GetProperty("opcode").GetUInt16(); var bytes = Bytes(row, "payload_hex"); var source = bytes.ToArray();
            var entry = Event(opcode, bytes); Array.Fill(bytes, (byte)0xa5); world.Apply(entry);
            Check(entry.Message.Payload.SequenceEqual(source), "owned wire including suffix");
            Check(counters.Kills.SelectMany(r => r.ToPayload()).SequenceEqual(Bytes(row, "kills_after")), "native network kill vector/count result");
            Check(counters.Collected.SelectMany(r => r.ToPayload()).SequenceEqual(Bytes(row, "collected_after")), "native category remapping, duplicates and collected counts");
            Check(counters.PendingCount == 0, "known baseline completes state mutation");
            var messages = row.GetProperty("messages");
            Check(counters.Messages.Recent.Count == messages.GetArrayLength(), "native HUD request count");
            for (var i = 0; i < messages.GetArrayLength(); i++)
            {
                var actual = counters.Messages.Recent[i]; var expected = messages[i];
                Check(actual.Mode == expected.GetProperty("mode").GetUInt32() && actual.Flags == expected.GetProperty("flags").GetUInt32(), "native HUD flags and mode");
                Check(actual.TextResource == (expected.GetProperty("resource").ValueKind == JsonValueKind.Null ? null : expected.GetProperty("resource").GetUInt32()), "native HUD resource or indirect text");
                if (expected.GetProperty("text").ValueKind == JsonValueKind.String)
                    Check(counters.Messages.Resolve(actual) == expected.GetProperty("text").GetString(), "native signed counter formatting and received table order");
            }
            var resolved = counters.Callbacks.Select(callback =>
            {
                Check(callback.TryResolve(out var index), "loaded receipt-time function baseline"); return index;
            }).Where(index => index.HasValue).Select(index => index!.Value).ToArray();
            Check(resolved.SequenceEqual(row.GetProperty("callbacks").EnumerateArray().Select(v => v.GetUInt32())), "native first callback name match and slot-zero fallback");
            Check(counters.CollectionAttempts.Count == (opcode is 0x1a2 or 0x1a3 ? 1 : 0), "collection attempted independently of counter flag");
            world.Apply(GoldGameEventDecoder.Decode(Bank(0x1b1)));
            Check(counters.Kills.SelectMany(r => r.ToPayload()).SequenceEqual(Bytes(row, "kills_after")), "empty bank preserves current counters");
            world.Clear(); Check(counters.Kills.Count == 0 && counters.Collected.Count == 0 && counters.Callbacks.Count == 0 &&
                counters.CollectionAttempts.Count == 0 && counters.Messages.ReceivedCount == 0 && counters.PendingCount == 0, "session cleanup");
        }
        Console.WriteLine($"PASS: {checks} counter receive native-result comparisons."); return checks;
    }
}
