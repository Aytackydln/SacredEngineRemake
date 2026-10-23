using System.Text.Json;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;

internal static class SessionLockChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Session locks: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-session-locks.json")));
        foreach (var row in fixture.RootElement.GetProperty("client_cases").EnumerateArray())
        {
            var opcode = row.GetProperty("opcode").GetUInt16(); var locked = opcode == GoldSessionLockCodec.LockOpcode;
            var before = row.GetProperty("before").GetUInt32(); var after = row.GetProperty("after").GetUInt32();
            Check((before & ~4u) == (after & ~4u), "native preserves every other session flag");
            if (row.GetProperty("enabled").GetInt32() != 0) Check(((after & 4) != 0) == locked, "native active connection lock flag");
            else Check(before == after, "native inactive flag gate");
            Check(row.GetProperty("notification").GetString() == (locked ? "UI_SESSION_LOCKED" : "UI_SESSION_UNLOCKED"), "native notification even when flag gate is inactive");
            var wire = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var expected = wire.ToArray();
            var entry = (GoldSessionLockEvent)GoldGameEventDecoder.Decode(new(opcode, 0, 0, 2, wire));
            Array.Fill(wire, (byte)0xcc); var world = new GoldWorldReplica();
            Check(world.Session.IsLocked is null, "lock initially unknown"); world.Apply(entry); world.Apply(entry);
            Check(world.Session.IsLocked == locked && world.Session.LockNotificationCount == 2, "opcode-derived state and repeated notification count");
            Check(world.Revision == 2 && world.Objects.Count == 0 && world.CreatureControls.PendingReplies == 0, "receipt does not create gameplay or callbacks");
            Check(entry.Message.Payload.SequenceEqual(expected), "owned ignored payload bytes");
            var admission = new GoldAdmissionTracker(); admission.Begin();
            admission.ProcessBatch([new(0x11,0,0,0,[4,3,0,0,0,0]),entry.Message]);
            Check(admission.Status.SessionLocked == locked && admission.Status.Phase == GoldAdmissionPhase.SlotAssigned &&
                admission.Status.WorldStartup is { HostWorldAvailable: true, AssignedClientHost: false, ParticipantStartApproved: false }, "session control preserves admission and startup roles");
            world.Clear(); Check(world.Session.IsLocked is null && world.Session.LockNotificationCount == 0, "disconnect cleanup");
        }
        foreach (var row in fixture.RootElement.GetProperty("server_cases").EnumerateArray())
        {
            var accepted = row.GetProperty("accepted").GetBoolean(); var opcode = row.GetProperty("opcode").GetUInt16();
            var before = row.GetProperty("before").GetUInt32(); var after = row.GetProperty("after").GetUInt32();
            Check(accepted == (row.GetProperty("compared_slot").GetUInt32() == row.GetProperty("designated_host").GetUInt32()), "native designated-host comparison");
            Check(after == (accepted ? opcode == 0x19 ? before & ~4u : before | 4u : before), "native authorized state change or rejected preservation");
            Check(row.GetProperty("relay_count").GetInt32() == (accepted ? 1 : 0), "native authorized broadcast only");
            Check(row.GetProperty("rejects").GetArrayLength() == (accepted ? 0 : 1), "native unauthorized rejection");
        }
        return checks;
    }
}
