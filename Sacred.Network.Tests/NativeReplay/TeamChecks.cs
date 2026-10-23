using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class TeamChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why)
        {
            if (!value) throw new Exception("Teams: " + why);
            checks++;
        }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-teams.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            uint[] Words(string key) => row.GetProperty(key).EnumerateArray().Select(value => value.GetUInt32()).ToArray();
            var before = Words("before");
            var world = new GoldWorldReplica();
            var local = row.GetProperty("local").GetByte();
            world.Apply(GoldGameEventDecoder.Decode(new(0x11, 0, 0, 0, [4, local, 0, 0, 0, 0])));
            world.Apply(GoldGameEventDecoder.Decode(Snapshot(before, row.GetProperty("outgoing_before").GetUInt32(), row.GetProperty("incoming_before").GetUInt32())));
            var opcode = row.GetProperty("opcode").GetUInt16();
            var wire = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!);
            var owned = wire.ToArray();
            var entry = GoldGameEventDecoder.Decode(new(opcode, 0, row.GetProperty("origin").GetByte(), 7, wire));
            Array.Fill(wire, (byte)0xcc);
            world.Apply(entry);
            var teams = world.Session.Teams;
            var after = Words("after");
            Check(teams.Slots.SequenceEqual(after), "native membership and leadership words");
            Check(teams.OutgoingInvitations == row.GetProperty("outgoing_after").GetUInt32(), "native outgoing invitation mask");
            Check(teams.IncomingInvitations == row.GetProperty("incoming_after").GetUInt32(), "native incoming invitation mask");
            Check(teams.LocalPlayerSlot == local && world.LocalPlayerSlot == 0, "admission establishes team identity before character upload");
            Check(teams.NotificationCount == 2 && world.Revision == 3, "each team notification counted once");
            Check(entry.Message.Payload.SequenceEqual(owned), "owned wire including ignored suffix");
            Check(teams.GetTeam(0) == 0 && teams.GetTeam(17) == 0 && !teams.IsLeader(0), "native invalid-slot query result");
            for (uint player = 1; player <= 16; player++)
            {
                Check(teams.GetTeam(player) == (after[player - 1] & 0x7fffffff), "native team query masks leadership");
                Check(teams.IsLeader(player) == ((after[player - 1] & 0x80000000) != 0), "retained leadership bit");
            }
            Check(world.CreatureControls.PendingReplies == 0 && world.Session.GameFlags is null, "no owned-creature callback or session-flag mutation");
            world.Clear();
            Check(teams.Slots.All(value => value == 0) && teams.IncomingInvitations == 0 && teams.OutgoingInvitations == 0 &&
                  teams.LocalPlayerSlot == 0 && teams.NotificationCount == 0 && !teams.HasSnapshot, "disconnect resets complete team state");
        }
        return checks;
    }

    private static GoldMessage Snapshot(uint[] slots, uint outgoing, uint incoming)
    {
        var bytes = new byte[72];
        for (var i = 0; i < 16; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), slots[i]);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64), outgoing);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(68), incoming);
        return new(0x13a, 0, 0, 0, bytes);
    }
}
