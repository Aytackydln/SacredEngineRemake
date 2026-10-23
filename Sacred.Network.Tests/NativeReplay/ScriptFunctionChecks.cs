using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;

internal static class ScriptFunctionChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Script functions: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-script-functions.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var count = row.GetProperty("count").GetInt32();
            var before = Convert.FromHexString(row.GetProperty("before_hex").GetString()!);
            var after = Convert.FromHexString(row.GetProperty("after_hex").GetString()!);
            var payload = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!);
            var index = row.GetProperty("index").GetUInt32();
            var vectors = new byte[4 + before.Length + 13]; // Later Vectoren.bin tables must stay outside the function vector.
            BinaryPrimitives.WriteInt32LittleEndian(vectors, count); before.CopyTo(vectors, 4);
            var world = new GoldWorldReplica(); var state = world.WorldBanks.Functions;
            state.Configure(vectors); Array.Fill(vectors, (byte)0xff);
            var entry = (GoldScriptFunctionEvent)GoldGameEventDecoder.Decode(new(0x1c4, 0, 1, 2, payload));
            var expected = payload.ToArray(); Array.Fill(payload, (byte)0xcc);
            var revision = state.Revision; world.Apply(entry);
            Check(state.Definitions.Count == count, "fixed native vector count");
            Check(state.Definitions.SelectMany(record => record.ToPayload()).SequenceEqual(after), "all native record bytes match");
            Check(state.Revision == revision + (index < count ? 1 : 0), "ignored index does not mutate state");
            Check(state.Updates.Count == (index < count ? 1 : 0), "only in-range replacements retained");
            Check(entry.Message.Payload.SequenceEqual(expected), "wire suffix and independent storage retained");
            var record = entry.Definition;
            Check(record.Start == BinaryPrimitives.ReadInt32LittleEndian(expected.AsSpan(68)) &&
                record.ByteLength == BinaryPrimitives.ReadInt32LittleEndian(expected.AsSpan(72)) &&
                record.Quest == BinaryPrimitives.ReadInt32LittleEndian(expected.AsSpan(76)) &&
                record.Number == BinaryPrimitives.ReadInt32LittleEndian(expected.AsSpan(80)) &&
                record.Deleted == BinaryPrimitives.ReadUInt32LittleEndian(expected.AsSpan(84)) &&
                record.DeletedFlag == expected[84], "mapped fields preserve signed words and deletion flag");
            var exported = record.ToPayload(); exported[0] ^= 255;
            Check(record.ToPayload().SequenceEqual(expected.AsSpan(4, 84).ToArray()), "immutable record export");
            Check(world.CreatureControls.PendingReplies == 0 && world.Objects.Count == 0, "no object or callback side effects");
            // Async campaign loading must converge to the same native result, including ignored indices.
            var originalVectors = new byte[4 + before.Length];
            BinaryPrimitives.WriteInt32LittleEndian(originalVectors, count); before.CopyTo(originalVectors, 4);
            var late = new GoldWorldReplica(); late.Apply(entry); late.WorldBanks.Functions.Configure(originalVectors);
            Check(late.WorldBanks.Functions.Definitions.SelectMany(r => r.ToPayload()).SequenceEqual(after), "receipt before campaign load");
            Check(late.WorldBanks.Functions.PendingCount == 0, "pending indices resolved against original count");
            world.Clear();
            Check(!state.IsConfigured && state.Definitions.Count == 0 && state.Updates.Count == 0 && state.PendingCount == 0, "disconnect clears campaign and updates");
        }
        return checks;
    }
}
