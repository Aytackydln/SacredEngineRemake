using System.Text.Json;
using Sacred.Network.Gameplay;

internal static class SessionTextChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Session text: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-session-text.json")));
        foreach (var row in fixture.RootElement.GetProperty("chat_cases").EnumerateArray())
        {
            var bytes = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var original = bytes.ToArray();
            var entry = (GoldChatEvent)GoldGameEventDecoder.Decode(new(0x32, 0, 31, 7, bytes)); Array.Fill(bytes, (byte)0xcc);
            Check(entry.Speaker == row.GetProperty("speaker").GetUInt32() && entry.Recipient == row.GetProperty("recipient").GetInt32(), "raw speaker and signed recipient");
            var expectedName = row.GetProperty("name_hex");
            Check(entry.Name == (expectedName.ValueKind == JsonValueKind.Null ? null : Text(expectedName.GetString()!)), "native name selection and full UTF-16 units");
            Check(entry.Text == Text(row.GetProperty("text_hex").GetString()!), "native text selection, including empty and surrogate words");
            var flags = row.GetProperty("ui_flags");
            Check(entry.IsServerMessage == (flags[0].GetUInt32() != 0) && entry.IsPrivate == (flags[1].GetUInt32() != 0), "native UI classification arguments");
            Check(flags[2].GetUInt32() == 0 && flags[3].GetUInt32() == 17, "native remaining delivery arguments");
            Check(entry.Message.Payload.SequenceEqual(original), "owned strings and retained suffix");
            var world = new GoldWorldReplica(); world.Apply(entry);
            Check(world.Session.Chat.ReceivedCount == 1 && world.Session.Chat.Messages.Single().Text == entry.Text, "shared ordered receive history");
            Check(world.CreatureControls.PendingReplies == 0 && world.Objects.Count == 0, "no invented reply or actor requirement");
            world.Clear(); Check(world.Session.Chat.Messages.Count == 0 && world.Session.Chat.ReceivedCount == 0, "disconnect cleanup");
        }
        foreach (var row in fixture.RootElement.GetProperty("cheat_cases").EnumerateArray())
        {
            var raw = row.GetProperty("raw_value").GetUInt32(); var before = row.GetProperty("before").GetUInt32();
            Check(row.GetProperty("after").GetUInt32() == (raw == 0 ? before & ~0x02000000u : before | 0x02000000), "native full flag word preserves unrelated bits");
            var bytes = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var copy = bytes.ToArray();
            var entry = (GoldCheatsEnabledEvent)GoldGameEventDecoder.Decode(new(0x1a8, 0, 0, 1, bytes)); Array.Fill(bytes, (byte)0xcc);
            var world = new GoldWorldReplica(); world.Apply(entry); world.Apply(entry);
            Check(entry.RawValue == raw && world.Session.CheatsEnabled == (raw != 0), "native truthiness, including noncanonical enabled words");
            Check(entry.Message.Payload.SequenceEqual(copy) && world.Session.CheatNotificationCount == 2, "ownership and repeated notifications");
            Check(world.Session.GameFlags is null && world.Session.IsPaused is null && world.Session.MaxPlayers is null && world.CreatureControls.PendingReplies == 0,
                "runtime cheat bit is independent of other session controls and callbacks");
            world.Clear(); Check(world.Session.CheatsEnabled is null && world.Session.CheatNotificationCount == 0, "unknown after disconnect");
        }
        return checks;
    }

    private static string Text(string hex)
    {
        var bytes = Convert.FromHexString(hex); var chars = new char[bytes.Length / 2];
        for (var i = 0; i < chars.Length; i++) chars[i] = (char)(bytes[2 * i] | bytes[2 * i + 1] << 8);
        return new string(chars);
    }
}
