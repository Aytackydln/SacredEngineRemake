using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using Sacred.Core.GameRes;
using Sacred.Network.Gameplay;

internal static class LobbyUpdateChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Lobby updates: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-lobby-updates.json")));
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(fixture.RootElement.GetProperty("ansi_code_page").GetInt32())!;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var opcode = row.GetProperty("opcode").GetUInt16();
            var world = new GoldWorldReplica(); var lobby = world.Session.Lobby;
            lobby.ConfigureChannels(new(row.GetProperty("local").GetBoolean(), row.GetProperty("globals").GetUInt32(),
                row.GetProperty("global_byte").GetBoolean()), encoding);
            world.Session.Notices.ConfigureResources(Resources(fixture.RootElement.GetProperty("localization").GetString()!));
            var bytes = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var expected = bytes.ToArray();
            var entry = GoldGameEventDecoder.Decode(new(opcode, 0, 31, 7, bytes)); Array.Fill(bytes, (byte)0xcc); world.Apply(entry);
            Check(entry.Message.Payload.SequenceEqual(expected), "owned wire data");
            var commands = row.GetProperty("commands").EnumerateArray().ToArray();
            switch (opcode)
            {
                case 0x27:
                    Check(lobby.LastProfile!.Value == commands[0].GetProperty("value").GetUInt32(), "profile value");
                    Check(lobby.LastProfile.Text == Text(commands[0]), "profile UTF-16 text");
                    Check(lobby.ProfileRequestCount == 1 && lobby.WindowRequestCount == 0, "one cEventProfile request"); break;
                case 0x2d: case 0x2f:
                    var request = lobby.LastWindowRequest!; var command = commands[0]; var args = command.GetProperty("args");
                    Check(request.NativeEventId == command.GetProperty("id").GetUInt32() && request.Target == command.GetProperty("target").GetString(), "UI event identity/target");
                    Check(request.Caption == command.GetProperty("caption").GetString(), "UI caption");
                    Check(request.NativeMode == args[0].GetUInt32() && request.NativeArgument == args[1].GetUInt32() && request.NativeFlags == args[2].GetUInt32(), "UI mode/value/flags");
                    Check(request.Text == Text(command), "player list text");
                    Check(lobby.NetworkWindowValue == (opcode == 0x2d ? row.GetProperty("controller_value").GetUInt32() : null), "controller+30 write only for 02D"); break;
                case 0x2e:
                    Check(lobby.ChannelClearRequestCount == 1 && commands[0].GetProperty("args")[0].GetUInt32() == 1, "list clear before rows");
                    var requests = lobby.ChannelRequests!;
                    Check(requests.Count == commands.Length - 1, "native filter predicate");
                    for (var i = 0; i < requests.Count; i++)
                    {
                        var native = commands[i + 1]; var nativeArgs = native.GetProperty("args");
                        Check(requests[i].SourceIndex == nativeArgs[1].GetInt32() && requests[i].Row.Flags == nativeArgs[2].GetUInt32(), "original list indices and flag words");
                        Check(requests[i].Text == Text(native), "signed counts, ANSI conversion and native display format");
                    }
                    Check(lobby.Channels.Count == BitConverter.ToUInt32(expected), "all source rows retained independently of filters"); break;
                case 0x31:
                    var notices = world.Session.Notices;
                    Check(notices.CloseRequestCount == 1 && notices.OpenRequestCount == 1 && notices.ChatRequestCount == 0, "kick dialog request order/count");
                    Check(notices.CurrentDialog!.Text == Text(commands[1]) && notices.CustomDialogText == Text(commands[1]), "localized kick text and mutable slot");
                    Check(notices.CurrentDialog.DialogAction == commands[1].GetProperty("args")[1].GetUInt32() &&
                        notices.CurrentDialog.NativeDialogParameter == commands[1].GetProperty("args")[4].GetUInt32(), "fixed action 1 and parameter 3"); break;
            }
            Check(world.Objects.Count == 0 && world.CreatureControls.PendingReplies == 0, "no invented gameplay/callback");
            world.Clear(); Check(lobby.Channels.Count == 0 && lobby.LastProfile is null && lobby.LastWindowRequest is null && lobby.ReceivedCount == 0, "disconnect cleanup");
        }
        return checks;
    }

    internal static GameResStore Resources(string text = "Lobby kick") => new(new Dictionary<uint, string>
    { [SacredResourceHash.Compute("UI_NET_LOBBYKICK")] = text }.ToFrozenDictionary());
    private static string? Text(JsonElement row)
    {
        var hex = row.GetProperty("text_hex"); if (hex.ValueKind == JsonValueKind.Null) return null;
        var bytes = Convert.FromHexString(hex.GetString()!); var chars = new char[bytes.Length / 2];
        for (var i = 0; i < chars.Length; i++) chars[i] = (char)(bytes[2 * i] | bytes[2 * i + 1] << 8);
        return new string(chars);
    }
}
