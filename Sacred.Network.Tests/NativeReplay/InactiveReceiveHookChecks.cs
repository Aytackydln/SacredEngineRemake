using System.Text.Json;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;

internal static class InactiveReceiveHookChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why)
        {
            if (!value) throw new Exception("Inactive receive hook: " + why);
            checks++;
        }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-inactive-receive-hooks.json")));
        foreach (var row in fixture.RootElement.GetProperty("client_cases").EnumerateArray())
        {
            var world = new GoldWorldReplica();
            world.Apply(GoldGameEventDecoder.Decode(new(0x1e, 0, 0, 0, [4, 0, 0, 0])));
            world.Apply(GoldGameEventDecoder.Decode(new(0x135, 0, 2, 0, [])));
            var revision = world.Revision; var session = world.SessionVersion;
            var wire = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var owned = wire.ToArray();
            var entry = GoldGameEventDecoder.Decode(new(row.GetProperty("opcode").GetUInt16(), 0, row.GetProperty("origin").GetByte(), 7, wire));
            Check(entry is GoldInactiveReceiveHookEvent, "explicit inactive-hook event");
            Array.Fill(wire, (byte)0xcc); world.Apply(entry);
            Check(entry.Message.Payload.SequenceEqual(owned), "all ignored bytes owned, including short/empty payloads");
            Check(row.GetProperty("stack_balanced").GetBoolean() && row.GetProperty("only_stack_access").GetBoolean() && row.GetProperty("controller_unchanged").GetBoolean(),
                "native hook never accesses payload or game state, even with an unmapped pointer");
            Check(world.Revision == revision && world.SessionVersion == session, "no world revision or session mutation");
            Check(world.Session.GameFlags == 4 && world.Session.IsLocked == true && world.Session.IsPaused == true && world.Session.PausedBy == 2,
                "received controls unchanged");
            Check(world.Objects.Count == 0 && world.Sectors.Count == 0 && world.CreatureControls.PendingReplies == 0 && world.WorldBanks.Functions.PendingCount == 0,
                "no world work or invented callback");
            var tracker = new GoldAdmissionTracker(); var before = tracker.Status;
            tracker.ProcessBatch([entry.Message]);
            Check(tracker.Status == before, "no admission state changes or slot requirement");
        }
        foreach (var row in fixture.RootElement.GetProperty("server_cases").EnumerateArray())
        {
            var destination = row.GetProperty("destination").GetUInt32(); var forwards = row.GetProperty("forwards");
            Check(forwards.GetArrayLength() == (destination <= 16 ? 1 : 0), "native server destination gate");
            if (forwards.GetArrayLength() == 0) continue;
            var relay = forwards[0];
            Check(relay.GetProperty("broadcast").GetBoolean() == (destination == 0), "broadcast zero versus player unicast");
            Check(relay.GetProperty("destination").GetUInt32() == (destination == 0 ? 17 : destination), "native relay destination");
            Check(relay.GetProperty("payload_hex").GetString() == row.GetProperty("payload_hex").GetString(), "server preserves entire routing-prefix payload");
        }
        return checks;
    }
}
