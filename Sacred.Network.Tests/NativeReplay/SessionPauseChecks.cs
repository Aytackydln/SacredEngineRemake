using System.Text.Json;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;

internal static class SessionPauseChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why)
        {
            if (!value) throw new Exception("Session pause: " + why);
            checks++;
        }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-session-pause.json")));
        foreach (var row in fixture.RootElement.GetProperty("client_cases").EnumerateArray())
        {
            var opcode = row.GetProperty("opcode").GetUInt16();
            var origin = row.GetProperty("origin").GetByte();
            var wire = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!);
            var owned = wire.ToArray();
            var entry = (GoldSessionPauseEvent)GoldGameEventDecoder.Decode(new(opcode, 0, origin, 7, wire));
            Array.Fill(wire, (byte)0xcc);
            var world = new GoldWorldReplica();
            var beforeOpcode = row.GetProperty("before_paused").GetInt32() != 0 ? (ushort)0x135 : (ushort)0x134;
            world.Apply(GoldGameEventDecoder.Decode(new(beforeOpcode, 0, row.GetProperty("before_origin").GetByte(), 0, [])));
            world.Apply(entry);
            var paused = row.GetProperty("timer_paused").GetInt32() != 0;
            Check(world.Session.IsPaused == paused && entry.IsPaused == paused, "native time-manager pause flag, including server origin zero");
            Check(world.Session.PausedBy == (paused ? origin : null), "controller origin versus separate pause flag");
            Check(row.GetProperty("controller_origin").GetUInt32() == (paused ? origin : 0), "native controller state");
            Check(entry.Message.Payload.SequenceEqual(owned), "ignored payload ownership");
            Check(world.Session.SimulationSeconds(0.125f) == (paused ? 0 : 0.125f), "simulation time gate");
            Check(world.Revision == 2 && world.Session.PauseNotificationCount == 2, "repeated receive notifications still run");
            var tracker = new GoldAdmissionTracker();
            tracker.ProcessBatch([entry.Message]); tracker.Begin(); tracker.ProcessBatch([new(0x11, 0, 0, 0, [4, 3, 0, 0, 0, 0])]);
            Check(tracker.Status.SessionPaused == paused && tracker.Status.PausedBy == world.Session.PausedBy, "early notification preserved through admission");
            Check(tracker.Status.Flags == 4 && !tracker.Status.WorldStartup.ParticipantStartApproved, "role and approval are separate");
            world.Clear();
            Check(world.Session.IsPaused is null && world.Session.PausedBy is null && world.Session.PauseNotificationCount == 0, "disconnect resets pause state");
        }
        foreach (var row in fixture.RootElement.GetProperty("server_cases").EnumerateArray())
        {
            var paused = row.GetProperty("paused").GetBoolean();
            var changed = row.GetProperty("before_paused").GetInt32() != (paused ? 1 : 0);
            Check(row.GetProperty("relay_count").GetInt32() == (changed ? 1 : 0), "server suppresses duplicate state relays");
            Check(row.GetProperty("menu_count").GetInt32() == (changed ? 1 : 0), "server state and menu change together");
        }
        return checks;
    }
}
