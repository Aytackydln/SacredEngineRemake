using System.Collections.Frozen;
using System.Text.Json;
using Sacred.Core.GameRes;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

internal static class LobbyLifecycleChecks
{
    public static int Run(string root)
    {
        var count = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Lobby lifecycle: " + why); count++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-lobby-lifecycle.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var op = row.GetProperty("opcode").GetUInt16(); var mode = row.GetProperty("mode").GetInt32();
            var sender = row.GetProperty("sender").GetByte(); var local = row.GetProperty("local").GetByte();
            var name = row.GetProperty("name").GetString()!; var world = new GoldWorldReplica();
            if (local is >= 1 and <= 16) world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Admission()));
            if (mode != 1) world.Apply(LobbyLifecyclePackets.Actor(2, mode == 2 ? "physical" : "creature", mode == 4 ? 0x10u : mode == 5 ? 2u : 0u,
                sector: mode is 5 or 6 ? (ushort)0 : (ushort)1, owner: mode == 6 ? (byte)3 : (byte)2, context: mode == 5 ? 3u : 0));
            world.Apply(LobbyLifecyclePackets.Actor(3, flags: mode == 4 ? 0x10u : 0u, sector: 2, owner: 3));
            world.Apply(GoldGameEventDecoder.Decode(LobbyLifecyclePackets.Roster(sender, name)));
            world.Apply(Event(0x2D, Words(row.GetProperty("window").GetUInt32())));
            var control = world.Session.Lobby.Control;
            control.ConfigureService(row.GetProperty("flagb").GetBoolean(), row.GetProperty("flagc").GetBoolean());
            control.ConfigureLocalDeparture(row.GetProperty("ready").GetBoolean(), row.GetProperty("flag28").GetBoolean());
            control.Settings.ConfigureSectorAvailability(_ => mode != 3);
            var patch = row.GetProperty("patch_hex");
            var wire = Bytes(row, "payload_hex");
            if (op == 0x2B) world.Session.Lobby.Transfers.ConfigurePatchSource(BitConverter.ToUInt32(wire, 4), patch.ValueKind == JsonValueKind.Null ? null : Convert.FromHexString(patch.GetString()!));
            var decoded = Event(op, wire, sender); var saved = wire.ToArray(); Array.Fill(wire, (byte)0xA5); world.Apply(decoded);
            Check(decoded.Message.Payload.SequenceEqual(saved), "owned message");
            var calls = row.GetProperty("calls").EnumerateArray().ToArray();
            JsonElement[] Calls(string kind) => calls.Where(c => c.GetProperty("kind").GetString() == kind).ToArray();
            var keys = row.GetProperty("keys").EnumerateArray().Select(k => k.GetString()!).Distinct();
            control.ConfigureResources(new GameResStore(keys.ToFrozenDictionary(k => SacredResourceHash.Compute(k), k => "<" + k + ">")));
            if (op == 2)
            {
                Check(world.Session.Notices.SessionEndRequestCount == 1 && world.Session.ClientHostRole.IsClientHost == false, "teardown and role reset");
                Check((world.Objects[3].BaseState.Flags & 0x10) != 0 == (mode == 4 || Calls("retire").Length != 0), "native foreign object cleanup");
                Check((world.Objects[2].BaseState.Flags & 0x10) != 0 == (mode == 4), "local object retained"); continue;
            }
            if (op == 0x00C)
            {
                var native = row.GetProperty("settings_fields").EnumerateArray().Select(v => v.GetUInt32()).ToArray(); var settings = control.Settings;
                Check(settings.ContextSector.GetValueOrDefault() == native[6], "native sector context");
                if (native[6] != 0)
                {
                    Check(settings.NativeWord9E08 == native[5], "native settings word 7");
                    if (mode != 3)
                    {
                        var f = settings.Fields!; Check(new[] { f.Flags, f.Word20, f.Word178, f.Word9158, f.Word9DB8 }.SequenceEqual(native.Take(5)), "direct settings stores and mask");
                    }
                    else Check(settings.Receipts.Single().Operations.Select(v => (v.Id, v.Value)).SequenceEqual(Calls("setting_operation").Select(v =>
                        (v.GetProperty("id").GetUInt32(), v.GetProperty("value").GetInt32()))), "fallback settings operation conversion");
                }
                continue;
            }
            if (op is 0x1C or 0x150 or 0x151)
            {
                Check(control.Native117RequestCount == Calls("publish").Length, "native local 117 decision");
                Check(control.ClockResetRequestCount == Calls("clock").Length, "native local clock request");
                Check(control.Notices.Count == Calls("chat").Length, "native local/remote/login notice gate");
                if (control.Notices.Count != 0) Check(control.Resolve(control.Notices.Single()) == Calls("chat").Single().GetProperty("text").GetString(), "native receipt-time names and punctuation");
                if (op != 0x151 && sender != local) Check(control.Roster.Find(sender)!.ToPayload().SequenceEqual(Bytes(row, "cleared_row_hex")), "834-byte remote row clear");
                continue;
            }
            if (op == 9)
            {
                var notice = control.Notices.Single(); var nativeChat = Calls("chat");
                Check(notice.IsChat == (nativeChat.Length != 0), "native chat/dialog classification");
                var expectedKey = keys.FirstOrDefault(k => k.Length != 0 && k != "UI_SPIELER");
                Check(notice.ResourceKey == expectedKey, "original error resource table");
                if (expectedKey is not null && nativeChat.Length != 0) Check(control.Resolve(notice) == nativeChat.Single().GetProperty("text").GetString(), "native error formatting");
                if (!notice.IsChat) Check(control.UiRequests.Single().Action == Calls("ui").Single().GetProperty("action").GetUInt32(), "native modal action");
                continue;
            }
            if (op is 0x24 or 0x26)
            {
                Check(control.UiRequests.Select(v => (v.EventId, v.Target, v.Mode, v.Action)).SequenceEqual(Calls("ui").Select(v =>
                    (v.GetProperty("event").GetUInt32(), v.GetProperty("target").GetString()!, v.GetProperty("mode").GetUInt32(), v.GetProperty("action").GetUInt32()))), $"native result UI operands {op:X3}/{BitConverter.ToUInt32(saved, 4)}");
                Check(control.ChannelQueries.SequenceEqual(Calls("query_channel").Select(v => v.GetProperty("channel").GetUInt32())), "native channel query range");
                Check(control.ServiceSettingRequestCount == Calls("service_setting").Length && control.OwnProfileRequestCount == Calls("own_profile").Length, "native service follow-ups");
                var custom = Calls("custom_text"); if (custom.Length != 0) Check(control.UiRequests[^1].Text == custom.Single().GetProperty("text").GetString(), "native custom error text");
                continue;
            }
            var transfers = world.Session.Lobby.Transfers; var publicCalls = Calls("public_profile");
            if (publicCalls.Length != 0)
            {
                var native = publicCalls.Single(); var valid = native.GetProperty("valid").GetBoolean();
                Check(transfers.PublicRequests.Count == (valid ? 1 : 0), "native public profile version/size gate");
                if (valid) Check(transfers.PublicRequests.Single().Profile.Bytes.Span.SequenceEqual(Bytes(native, "normalized_hex")), "native legacy profile normalization");
            }
            var profiles = Calls("player_profile"); Check(transfers.PlayerRequests.Count == profiles.Length, "native player profile transfer branch");
            if (profiles.Length != 0)
            {
                var work = transfers.PlayerRequests.Single(); Check(work.Profile.Span.SequenceEqual(Bytes(profiles[0], "profile_hex")), "native 128-byte optional patch/556-byte copy");
                Check(work.PageIndex == Calls("page").Single().GetProperty("index").GetUInt16(), "native selector-1 page index");
            }
        }
        foreach (var row in fixture.RootElement.GetProperty("packed_cases").EnumerateArray())
            Check(GoldPackedProfileBlock.Decode(Bytes(row, "encoded_hex")).SequenceEqual(Bytes(row, "decoded_hex")), "original raw inner rolling-XOR decoder");
        return count;
    }
}
