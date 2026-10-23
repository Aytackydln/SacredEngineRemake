using System.Text.Json;
using Sacred.Network.Gameplay;

internal static class ScriptTableChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Script tables: " + why); checks++; }
        GoldGameEvent Event(ushort opcode, byte[] bytes) => GoldGameEventDecoder.Decode(new(opcode, 0, 1, 2, bytes));
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-script-tables.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var opcode = row.GetProperty("opcode").GetUInt16(); var world = new GoldWorldReplica();
            var state = world.WorldBanks.ScriptTables;
            Check(state.Tables.Count == 0, "unreceived tables are absent");
            foreach (var op in new ushort[] { 0x1d3, 0x1d4, 0x1d5 }) world.Apply(Event(op, new byte[op == 0x1d5 ? 36 : 228]));
            var previous = state.Tables.ToDictionary(pair => pair.Key, pair => pair.Value);
            var payload = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var expected = payload.ToArray();
            var entry = (GoldScriptTableEvent)Event(opcode, payload); Array.Fill(payload, (byte)0xff);
            Check(entry.Snapshot.ToPayload().SequenceEqual(expected) && entry.Message.Payload.SequenceEqual(expected), "independent owned message and table storage");
            var revision = world.Revision; world.Apply(entry);
            var current = state.Tables[opcode];
            Check(current.Count == row.GetProperty("count").GetInt32() && current.RecordSize == row.GetProperty("record_size").GetInt32(), "native floor(length/stride) count");
            Check(current.ToPayload().SequenceEqual(expected), "complete native byte copy retained");
            Check(current.RemainingData.SequenceEqual(Convert.FromHexString(row.GetProperty("tail_hex").GetString()!)), "partial record bytes retained separately");
            var records = new List<byte>();
            for (var index = 0; index < current.Count; index++) records.AddRange(current.GetRecord(index).ToArray());
            Check(records.SequenceEqual(Convert.FromHexString(row.GetProperty("records_hex").GetString()!)), "logical records match native vector contents");
            Check(previous.Where(pair => pair.Key != opcode).All(pair => ReferenceEquals(state.Tables[pair.Key], pair.Value)), "unrelated native vectors unchanged");
            Check(world.Revision == revision+1 && world.Objects.Count == 0 && world.CreatureControls.PendingReplies == 0, "accepted replacement without object or callback effects");
            var exported = current.ToPayload(); if (exported.Length > 0) exported[0] ^= 255;
            Check(current.ToPayload().SequenceEqual(expected), "export cannot mutate retained snapshot");
            foreach (var index in new[] { -1, current.Count })
            {
                try { current.GetRecord(index); throw new Exception("out-of-range record accepted"); }
                catch (ArgumentOutOfRangeException) { checks++; }
            }
            world.Clear(); Check(state.Tables.Count == 0, "disconnect clears table presence and contents");
        }
        var forgedWorld = new GoldWorldReplica();
        forgedWorld.Apply(new GoldScriptTableEvent(new(0x1d3, 0, 1, 0, new byte[76]), GoldScriptTableSnapshot.FromPayload(0x1d5, new byte[36])));
        Check(forgedWorld.WorldBanks.ScriptTables.Tables.Count == 1 && forgedWorld.WorldBanks.ScriptTables.Tables[0x1d3].Count == 1, "wire opcode and payload override conflicting parsed metadata");
        return checks;
    }
}
