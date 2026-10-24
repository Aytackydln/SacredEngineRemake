using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using Sacred.Core.GameRes;
using Sacred.Network.Gameplay;

internal static class SessionBrowserChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Session/browser: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-session-browser.json")));
        var data = fixture.RootElement;
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(data.GetProperty("ansi_code_page").GetInt32())!;
        var resources = new GameResStore(data.GetProperty("localizations").EnumerateObject()
            .ToFrozenDictionary(p => SacredResourceHash.Compute(p.Name), p => p.Value.GetString()!));
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var world = new GoldWorldReplica(); var browser = world.Session.Lobby.ServerBrowser;
            browser.ConfigureNameEncoding(encoding); world.Session.Notices.ConfigureResources(resources);
            world.Apply(GoldGameEventDecoder.Decode(new(0x11, 0, 0, 0, [0, 7, 0, 0, 0, 0])));
            world.Apply(GoldGameEventDecoder.Decode(new(0x2d, 0, 0, 0, BitConverter.GetBytes(7u))));
            if (row.TryGetProperty("initial", out var initial))
                foreach (var server in initial.EnumerateArray()) world.Apply(GoldGameEventDecoder.Decode(new(0x2a, 0, 0, 0, Wire(server, encoding))));
            if (row.TryGetProperty("window", out var window))
                world.Apply(GoldGameEventDecoder.Decode(new(0x2d, 0, 0, 0, BitConverter.GetBytes(window.GetUInt32()))));
            if (row.TryGetProperty("blocked", out var blocked)) browser.SuppressPlayerRefresh = blocked.GetBoolean();
            var opcode = row.GetProperty("opcode").GetUInt16();
            // Match native manager slots: local, server-owned, inactive remote, active remote.
            foreach (var (id, flags, owner) in new (uint, uint, byte)[] { (1, 0, 7), (2, 0, 0), (3, 0x10, 0), (4, 0, 2) })
                world.Apply(Object(id, flags, owner));
            var payload = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var expected = payload.ToArray();
            var entry = GoldGameEventDecoder.Decode(new(opcode, 0, 31, 7, payload)); Array.Fill(payload, (byte)0xcc);
            var wasChanged = browser.ListChanged; var browserRevision = browser.Revision;
            world.Apply(entry); Check(entry.Message.Payload.SequenceEqual(expected), "owned wire payload");
            var expectedRows = row.GetProperty("rows").EnumerateArray().ToArray();
            Check(browser.Servers.Count == expectedRows.Length, "native append/update/removal/reset count");
            for (var i = 0; i < expectedRows.Length; i++) CheckRow(browser.Servers[i], expectedRows[i]);
            if (opcode is 0x2a or 0x2c)
            {
                var changed = row.GetProperty("list_changed").GetByte() != 0;
                Check(browser.ListChanged == (wasChanged || changed), "native dirty flag preserves earlier receipts");
                Check(browser.Revision - browserRevision == (changed ? 1 : 0), "receipt-time window gate precedes oversized reset");
            }
            if (opcode == 0x47) CheckRow(browser.Current!, row.GetProperty("current"));
            var commands = row.GetProperty("commands").EnumerateArray().ToArray();
            if (opcode == 0x30)
            {
                var requests = row.GetProperty("requests");
                Check(browser.RefreshRequestCount == requests.GetArrayLength(), "native manager+2B refresh gate");
                Check((browser.LastPlayerRefresh is not null) == (commands.Length != 0), "refresh event gate");
                if (browser.LastPlayerRefresh is { } refresh)
                {
                    Check(commands.Length == 2 && commands[0].GetProperty("kind").GetString() == "ui" &&
                        commands[1].GetProperty("kind").GetString() == "profile", "UI then profile event order");
                    var ui = commands[0]; var args = ui.GetProperty("args");
                    Check(refresh.NativeEventId == ui.GetProperty("id").GetUInt32() && refresh.Target == ui.GetProperty("target").GetString() &&
                        refresh.Caption == ui.GetProperty("caption").GetString(), "native refresh UI identity");
                    Check(refresh.NativeMode == args[0].GetUInt32() && refresh.Value == args[1].GetUInt32() &&
                        refresh.NativeFlags == args[2].GetUInt32(), "refresh UI mode/value/flags");
                    Check(refresh.Text == Text(ui.GetProperty("text_hex").GetString()!) &&
                        refresh.Text == Text(commands[1].GetProperty("text_hex").GetString()!), "unmodified UTF-16 refresh strings");
                    Check(refresh.Value == requests[0].GetUInt32() && refresh.Value == commands[1].GetProperty("value").GetUInt32(), "profile event and follow-up request value");
                }
            }
            if (entry is GoldSessionReceiveControlEvent)
                Check(world.Session.NativeReceiveControlEnabled == (row.GetProperty("control_byte").GetByte() != 0), "original singleton byte write");
            if (entry is GoldSessionNoticeEvent notice)
            {
                var state = world.Session.Notices; var dialog = state.CurrentDialog!;
                Check(state.CloseRequestCount == 1 && state.OpenRequestCount == 1 && commands.Length == 2, "ordered close/open requests");
                var args = commands[1].GetProperty("args");
                Check(dialog.DialogAction == args[1].GetUInt32() && dialog.NativeDialogParameter == args[4].GetUInt32(), "fixed dialog action/parameter even for long localization");
                Check(dialog.Text == Text(commands[1].GetProperty("text_hex").GetString()!), "original localized dialog text");
                Check(state.ChatRequestCount == row.GetProperty("chats").GetArrayLength(), "lost-connection system chat");
                if (row.GetProperty("chats").GetArrayLength() != 0)
                {
                    var chat = row.GetProperty("chats")[0];
                    Check(dialog.NativeChatSelector == chat.GetProperty("flags")[3].GetUInt32() &&
                        dialog.Text == Text(chat.GetProperty("text_hex").GetString()!), "system chat selector and localized body survive modal dialog");
                }
                Check(state.SessionEndRequestCount == row.GetProperty("calls").EnumerateArray().Count(c => c.GetString() == "disconnect"), "native disconnect request");
                Check(state.CustomDialogText is null && notice.UpdatesCustomText == false, "connection notices leave custom text slot alone");
                var removed = row.GetProperty("removed").EnumerateArray().Select(p => p.GetUInt32()).ToHashSet();
                foreach (var (id, flags) in new (uint, uint)[] { (1, 0), (2, 0), (3, 0x10), (4, 0) })
                    Check(world.Objects[id].BaseState.Flags == (removed.Contains(id) ? flags | 0x10 : flags), "native active remote-object cleanup selection");
            }
            world.Clear(); Check(browser.Servers.Count == 0 && browser.Current is null && browser.LastPlayerRefresh is null &&
                world.Session.NativeReceiveControlEnabled is null && world.Session.Notices.SessionEndRequestCount == 0, "session cleanup");
        }
        return checks;

        void CheckRow(GoldLobbyServerView actual, JsonElement native)
        {
            Check(actual.Record.Words.SequenceEqual(native.GetProperty("words").EnumerateArray().Select(x => x.GetUInt32())), "all eight native words");
            Check(actual.Name == Text(native.GetProperty("name_hex").GetString()!), $"ANSI server name conversion: actual '{actual.Name}', expected '{Text(native.GetProperty("name_hex").GetString()!)}'");
            Check(actual.Record.EntryKey == native.GetProperty("entry_key").GetUInt32() && actual.Record.WindowValue == native.GetProperty("window_value").GetUInt32(), "native tail words");
        }
    }

    private static GoldObjectEvent Object(uint id, uint flags, byte owner)
    {
        var state = new GoldObjectBaseState(id, 100, flags, 1, 10, 20, 0, 0, 0, 0, owner, 0);
        return (GoldObjectEvent)GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16e, 1, state.ToPayload()));
    }
    private static byte[] Wire(JsonElement row, Encoding encoding)
    {
        var bytes = new byte[200]; var words = row.GetProperty("words").EnumerateArray().ToArray();
        for (var i = 0; i < 8; i++) BitConverter.GetBytes(words[i].GetUInt32()).CopyTo(bytes, i * 4);
        encoding.GetBytes(Text(row.GetProperty("name_hex").GetString()!)).CopyTo(bytes, 32);
        BitConverter.GetBytes(row.GetProperty("entry_key").GetUInt32()).CopyTo(bytes, 192);
        BitConverter.GetBytes(row.GetProperty("window_value").GetUInt32()).CopyTo(bytes, 196);
        return bytes;
    }
    private static string Text(string hex)
    {
        var bytes = Convert.FromHexString(hex); var chars = new char[bytes.Length / 2];
        for (var i = 0; i < chars.Length; i++) chars[i] = (char)BitConverter.ToUInt16(bytes, i * 2);
        return new string(chars);
    }
}
