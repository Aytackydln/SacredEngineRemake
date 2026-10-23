using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class ChannelChatHostRoleChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Channel chat/host role: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-channel-chat-host-role.json")));
        foreach (var row in fixture.RootElement.GetProperty("chat_cases").EnumerateArray())
        {
            var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(Roster("Player")));
            if (row.GetProperty("connected").GetBoolean()) world.Apply(GoldGameEventDecoder.Decode(Admission(false)));
            var mask = row.GetProperty("ignore_mask").GetUInt16();
            for (byte slot = 1; slot <= 16; slot++) world.Session.ChannelChat.SetPlayerIgnored(slot, (mask & (1 << (slot - 1))) != 0);
            var wire = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var original = wire.ToArray();
            var entry = (GoldChannelChatEvent)GoldGameEventDecoder.Decode(new(row.GetProperty("opcode").GetUInt16(), 0, row.GetProperty("origin").GetByte(), 7, wire));
            Array.Fill(wire, (byte)0xcc); world.Apply(entry); var state = world.Session.ChannelChat;
            var delivered = row.GetProperty("delivered").GetBoolean();
            Check(state.Messages.Count == (delivered ? 1 : 0) && state.SuppressedCount == (delivered ? 0 : 1), "native origin/mute gates");
            Check(state.ReceivedCount == 1 && state.IgnoredPlayersMask == mask, "receipt count and unchanged local preferences");
            Check(entry.Message.Payload.SequenceEqual(original) && entry.Text == Text(row.GetProperty("text_hex").GetString()!), "wire ownership and UTF-16 units");
            if (delivered)
            {
                var delivery = state.Messages.Single();
                Check(delivery.IncludeSpeaker == row.GetProperty("include_speaker").GetBoolean(), "native active versus pre-session formatting");
                var name = row.GetProperty("name_hex");
                Check(delivery.SpeakerName == (name.ValueKind == JsonValueKind.Null ? null : Text(name.GetString()!)), "receipt-time cached player name");
                var flags = row.GetProperty("ui_flags")[0];
                Check(entry.IsServerMessage == (flags[0].GetUInt32() != 0), "server classification uses origin");
                Check((entry.Channel == GoldChatChannel.Private) == (flags[1].GetUInt32() != 0) &&
                    (entry.Channel == GoldChatChannel.Team) == (flags[2].GetUInt32() != 0), "private/team UI flags");
                Check(flags[3].GetUInt32() == entry.Message.Origin, "native speaker delivery identifier");
            }
            Check(world.CreatureControls.PendingReplies == 0, "no invented callback");
            world.Clear(); Check(state.Messages.Count == 0 && state.LastDelivery is null && state.IgnoredPlayersMask == 0, "disconnect cleanup");
        }
        foreach (var row in fixture.RootElement.GetProperty("role_cases").EnumerateArray())
        {
            var admission = Admission(row.GetProperty("before").GetByte() != 0);
            var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(admission));
            var tracker = new GoldAdmissionTracker(); tracker.Begin(); tracker.ProcessBatch([admission, new(0x17, 0, 0, 0, [])]);
            var before = tracker.Status;
            var wire = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var original = wire.ToArray();
            var message = new GoldMessage(row.GetProperty("opcode").GetUInt16(), 0, 0, 7, wire);
            var entry = GoldGameEventDecoder.Decode(message); Array.Fill(wire, (byte)0xcc); world.Apply(entry); tracker.ProcessBatch([entry.Message]);
            var state = world.Session.ClientHostRole; var expected = row.GetProperty("after").GetByte() != 0;
            Check(state.IsClientHost == expected && tracker.Status.WorldStartup.AssignedClientHost == expected, "native role in replica and transport authorization");
            Check(state.NotificationCount == 1 && state.ChangeCount == (row.GetProperty("changed").GetBoolean() ? 1 : 0), "repeated native role notifications suppressed");
            var keys = row.GetProperty("localization_keys");
            Check(state.LastNotificationKey == (keys.GetArrayLength() == 0 ? null : keys[0].GetString()), "native role UI selection");
            Check(tracker.Status.WorldStartup.HostWorldAvailable == before.WorldStartup.HostWorldAvailable && tracker.Status.WorldStartup.ParticipantStartApproved &&
                tracker.Status.Flags == before.Flags && tracker.Status.PlayerSlot == before.PlayerSlot, "role transfer preserves approval and initial admission flags");
            Check(entry.Message.Payload.SequenceEqual(original) && world.CreatureControls.PendingReplies == 0, "ignored payload ownership without owner callbacks");
        }
        return checks;
    }

    internal static GoldMessage Admission(bool host) => new(0x11, 0, 0, 0, [(byte)(4 | (host ? 1 : 0)), 3, 0, 0, 0, 0]);
    internal static GoldMessage Roster(string prefix)
    {
        var bytes = new byte[GoldPlayerRosterLayout.Size];
        for (var slot = 1; slot <= 16; slot++) WriteText(bytes, slot * GoldPlayerDataLayout.Size + GoldPlayerDataLayout.NameOffset, prefix + slot);
        return new(0x1b, 0, 0, 0, bytes);
    }
    internal static void WriteText(byte[] bytes, int offset, string text)
    {
        for (var i = 0; i < text.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + i * 2), text[i]);
    }
    private static string Text(string hex)
    {
        var bytes = Convert.FromHexString(hex); var chars = new char[bytes.Length / 2];
        for (var i = 0; i < chars.Length; i++) chars[i] = (char)(bytes[2 * i] | bytes[2 * i + 1] << 8);
        return new string(chars);
    }
}
