using System.Collections.Frozen;
using System.Text.Json;
using Sacred.Core.GameRes;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class SessionNoticeChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Session notices: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-session-notices.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var texts = row.GetProperty("localizations_hex").EnumerateObject().ToDictionary(p => p.Name, p => Text(p.Value.GetString()!));
            var world = new GoldWorldReplica(); var state = world.Session.Notices;
            state.ConfigureResources(Resources(texts["UI_ERROR_BUDDYLIST_ADD"], texts["ERROR_TINCAT_CONNECT_FAILED"], texts["ERROR_TINCAT_CONNECTABORT"]));
            var bytes = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var owned = bytes.ToArray();
            var entry = GoldGameEventDecoder.Decode(new(row.GetProperty("opcode").GetUInt16(), 0, row.GetProperty("origin").GetByte(), 7, bytes));
            Array.Fill(bytes, (byte)0xcc); world.Apply(entry); var view = state.LastReceipt!;
            Check(entry is GoldSessionNoticeEvent && entry.Message.Payload.SequenceEqual(owned), "typed owned receipt, including unread suffix/abort payload");
            Check(view.Text == Text(row.GetProperty("text_hex").GetString()!), "native localized/received text composition and UTF-16 units");
            Check(view.Event.IsModal == row.GetProperty("modal").GetBoolean(), "native modal byte selection");
            var commands = row.GetProperty("commands").EnumerateArray().ToArray();
            Check(state.CloseRequestCount == commands.Count(cmd => cmd.GetProperty("action").GetUInt32() == 4), "native DEFAULT_BUSYDLG close requests");
            Check(state.OpenRequestCount == commands.Count(cmd => cmd.GetProperty("action").GetUInt32() != 4), "native open requests");
            if (view.Event.IsModal)
            {
                Check(state.CurrentDialog?.DialogAction == commands[^1].GetProperty("action").GetUInt32(), "native 150-unit threshold or fixed abort action");
                Check(state.CurrentDialog?.Text == Text(commands[^1].GetProperty("text_hex").GetString()!), "native dialog body");
            }
            else Check(state.CurrentDialog is null && view.DialogAction is null, "nonmodal messages do not open a dialog");
            var chat = row.GetProperty("chat");
            Check(state.ChatRequestCount == chat.GetArrayLength(), "native system chat requests");
            if (chat.GetArrayLength() != 0)
            {
                Check(view.NativeChatSelector == chat[0].GetProperty("flags")[3].GetUInt32(), "native chat selector 17 versus zero");
                Check(chat[0].GetProperty("flags")[0].GetUInt32() == 1, "native server-chat presentation flag");
            }
            Check(state.CustomDialogText == (row.GetProperty("custom_writes").GetInt32() == 0 ? null : view.Text), "native mutable resource 0x26FF assignment");
            Check(state.ReceivedCount == 1 && state.Recent.Count == 1 && world.Objects.Count == 0 && world.CreatureControls.PendingReplies == 0,
                "receipt state without invented world mutation or callback");
            world.Clear(); Check(state.Recent.Count == 0 && state.CurrentDialog is null && state.CustomDialogText is null && state.ReceivedCount == 0, "disconnect cleanup");
        }
        return checks;
    }

    internal static GameResStore Resources(string buddy = "Added", string failed = "Failure", string aborted = "Aborted") =>
        new(new Dictionary<uint, string>
        {
            [SacredResourceHash.Compute("UI_ERROR_BUDDYLIST_ADD")] = buddy,
            [SacredResourceHash.Compute("ERROR_TINCAT_CONNECT_FAILED")] = failed,
            [SacredResourceHash.Compute("ERROR_TINCAT_CONNECTABORT")] = aborted
        }.ToFrozenDictionary());

    internal static GoldMessage Message(ushort opcode, string text = "", byte modal = 0)
    {
        if (opcode == 0x3a) return new(opcode, 0, 0, 0, []);
        var offset = opcode == 0x35 ? 12 : opcode == 0x3c ? 2 : 6;
        var bytes = new byte[offset + 2 + text.Length * 2]; bytes.AsSpan(0, offset).Fill(0xa5);
        if (opcode == 0x3d) bytes[4] = modal;
        ChannelChatHostRoleChecks.WriteText(bytes, offset, text); return new(opcode, 0, 31, 7, bytes);
    }
    private static string Text(string hex)
    {
        var bytes = Convert.FromHexString(hex); var chars = new char[bytes.Length / 2];
        for (var i = 0; i < chars.Length; i++) chars[i] = (char)(bytes[2 * i] | bytes[2 * i + 1] << 8);
        return new string(chars);
    }
}
