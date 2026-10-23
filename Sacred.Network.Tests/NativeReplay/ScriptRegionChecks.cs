using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class ScriptRegionChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Script regions: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-script-regions.json")));
        var caseIndex = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before = Groups(row.GetProperty("before")); var after = Groups(row.GetProperty("after"));
            var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(Bank(before)));
            var bytes = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var expected = bytes.ToArray();
            var entry = (GoldScriptRegionEvent)GoldGameEventDecoder.Decode(new(0x1c5, 0, 1, 2, bytes));
            Array.Fill(bytes, (byte)0xcc); var revision = world.Revision; world.Apply(entry);
            var state = world.WorldBanks.ScriptRegions;
            Check(state.Regions.Count == after.Length, "native keyed table size, including retained empty vectors");
            foreach (var group in after)
                Check(state.Regions[group.Key].SelectMany(r => r.ToPayload()).SequenceEqual(group.Data),
                    $"case {caseIndex}, mode {entry.Mode}, key {group.Key}: complete native vector bytes, copies and erasure order ({state.Regions[group.Key].Count} managed / {group.Data.Length/32} native)");
            Check(world.Revision == revision+1 && world.Objects.Count == 0 && world.CreatureControls.PendingReplies == 0, "receipt without actor/audio/callback simulation");
            Check(entry.Message.Payload.SequenceEqual(expected) && entry.Region.ToPayload().SequenceEqual(expected.AsSpan(12, 32).ToArray()), "wire ownership and retained suffix");
            world.Clear();
            Check(state.Regions.Count == 0, "session cleanup");
            caseIndex++;
        }
        Check(fixture.RootElement.GetProperty("duplicate_bank_last_wins").GetBoolean(), "native repeated bank key assignment");
        Check(fixture.RootElement.GetProperty("zero_bank_clears").GetBoolean(), "native unconditional bank clear");
        return checks;
    }

    private static (uint Key, byte[] Data)[] Groups(JsonElement source) => source.EnumerateArray().Select(group =>
        (group.GetProperty("key").GetUInt32(), group.GetProperty("runs").EnumerateArray().SelectMany(run =>
            Enumerable.Repeat(Convert.FromHexString(run.GetProperty("record_hex").GetString()!), run.GetProperty("count").GetInt32())
                .SelectMany(bytes => bytes)).ToArray())).ToArray();

    internal static GoldMessage Bank(params (uint Key, byte[] Data)[] groups)
    {
        var records = groups.Select(group =>
        {
            var bytes = new byte[8+group.Data.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, group.Key);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)group.Data.Length/32));
            group.Data.CopyTo(bytes, 8); return bytes;
        }).ToArray();
        return ScriptResourceChecks.Bank(0x1b1, 5, records);
    }
}
