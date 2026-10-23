using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class SessionPlayerLimitChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why)
        {
            if (!value) throw new Exception("Session player limit: " + why);
            checks++;
        }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-session-player-limit.json")));
        foreach (var row in fixture.RootElement.GetProperty("client_cases").EnumerateArray())
        {
            var flags = row.GetProperty("game_flags").GetUInt32();
            var before = row.GetProperty("before").GetUInt32();
            var after = row.GetProperty("after").GetUInt32();
            var announced = row.GetProperty("announced").GetUInt32();
            var wire = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!);
            var expected = wire.ToArray();
            var entry = (GoldSessionPlayerLimitEvent)GoldGameEventDecoder.Decode(new(0x23, 0, 0, 0, wire));
            Array.Fill(wire, (byte)0xcc);
            var world = new GoldWorldReplica();
            var tracker = new GoldAdmissionTracker();
            void Apply(GoldMessage message) { tracker.ProcessBatch([message]); world.Apply(GoldGameEventDecoder.Decode(message)); }
            Apply(Word(0x1e, 0));
            Apply(Word(0x23, before));
            Apply(Word(0x1e, flags));
            Check(world.Session.MaxPlayers == before, "flags alone do not recalculate capacity");
            tracker.ProcessBatch([entry.Message]);
            world.Apply(entry);
            Check(world.Session.MaxPlayers == after && tracker.Status.MaxPlayers == after, "native receipt-time capacity for either connection guard state");
            Check(entry.AnnouncedMaxPlayers == announced && world.Session.AnnouncedMaxPlayers == announced && tracker.Status.AnnouncedMaxPlayers == announced,
                "raw announcement is separate from effective capacity");
            Check(entry.Message.Payload.SequenceEqual(expected), "owned word and ignored suffix");
            Check(world.Session.GameFlags == flags && tracker.Status.SessionGameFlags == flags, "capacity does not change session flags");
            Check(world.Revision == 4 && world.Session.PlayerLimitNotificationCount == 2, "zero and duplicate messages still count");
            tracker.Begin();
            tracker.ProcessBatch([new(0x11, 0, 0, 0, [4, 3, 0, 0, 0, 0])]);
            Check(tracker.Status.MaxPlayers == after && tracker.Status.AnnouncedMaxPlayers == announced, "early state survives admission");
            Check(tracker.Status.Flags == 4 && !tracker.Status.WorldStartup.ParticipantStartApproved, "no host-role or world-start approval mutation");
            world.Clear();
            Check(world.Session.MaxPlayers is null && world.Session.AnnouncedMaxPlayers is null && world.Session.PlayerLimitNotificationCount == 0,
                "disconnect clears observations");
        }
        foreach (var row in fixture.RootElement.GetProperty("server_cases").EnumerateArray())
        {
            var flags = row.GetProperty("game_flags").GetUInt32();
            var announced = row.GetProperty("announced").GetUInt32();
            var clamped = Math.Min(announced, (flags & 0x40010) != 0 ? 4u : 16u);
            var accepted = row.GetProperty("accepted").GetBoolean();
            Check(accepted == (row.GetProperty("slot").GetUInt32() == 3 && row.GetProperty("client_gate").GetInt32() == 0 && (flags & 8) == 0),
                "native server authorization gates");
            Check(row.GetProperty("after").GetUInt32() == (accepted && clamped != 0 ? clamped : row.GetProperty("before").GetUInt32()),
                "native server clamp and zero/rejection preservation");
            var relays = row.GetProperty("relayed_limits");
            Check(relays.GetArrayLength() == (accepted ? 1 : 0), "native relay only for authorized requests");
            if (accepted) Check(relays[0].GetUInt32() == clamped, "relays clamped word, including zero");
            var backend = row.GetProperty("backend");
            Check(backend.GetArrayLength() == (accepted && clamped != 0 ? 1 : 0), "zero does not call backend setter");
            if (backend.GetArrayLength() != 0) Check(backend[0].GetUInt32() == clamped, "backend receives clamped capacity");
        }
        return checks;
    }

    private static GoldMessage Word(ushort opcode, uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return new(opcode, 0, 0, 0, bytes);
    }
}
