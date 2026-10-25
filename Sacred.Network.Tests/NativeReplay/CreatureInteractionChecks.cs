using System.Collections.Frozen;
using System.Text.Json;
using Sacred.Core.GameRes;
using Sacred.Core.Pak.Items;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

internal static class CreatureInteractionChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Creature interactions: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-interactions.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Admission()));
            var op = row.GetProperty("opcode").GetUInt16(); var mode = row.GetProperty("mode").GetInt32();
            if (op == 0x18b) world.ConfigureObjectCategories(new Dictionary<uint, SacredItemCategory> { [1] = SacredItemCategory.Container });
            if (mode != 1) world.Apply(CreatureCommandPackets.Actor(2, mode == 3 ? "physical" : "creature", mode == 2 ? 0x10u : 0u));
            if (mode != 4) world.Apply(op == 0x18b && mode != 6 ? Actor(mode == 5 ? 0x10u : 0u, container: true) :
                CreatureCommandPackets.Actor(500, mode == 6 ? "physical" : "creature", mode == 5 ? 0x10u : 0u));
            var questPairs = op == 0x145 ? row.GetProperty("quest_initial").EnumerateArray().SelectMany(q => q.EnumerateArray().Select(v => v.GetUInt32())).ToArray() : [];
            if (mode != 4) world.Apply(GoldGameEventDecoder.Decode(CreatureInteractionPackets.Inventory(questPairs)));
            var buddy = world.Session.Lobby.Buddies;
            GoldBuddyRecord Buddy(JsonElement b) => new(b.GetProperty("id").GetUInt32(), b.GetProperty("status").GetUInt32(), b.GetProperty("name").GetString()!,
                b.GetProperty("game").GetUInt32(), b.GetProperty("game_name").GetString()!, b.GetProperty("channel").GetInt32(), b.GetProperty("channel_name").GetString()!);
            var initial = row.GetProperty("initial").EnumerateArray().ToArray();
            if (op == 0x36) buddy.Seed(initial.Select(Buddy));
            var region = world.CreatureCommands.Interactions.RegionTypes;
            if (op == 0x189)
            {
                for (var i = 0; i < 50; i++) region.Seed(i, new byte[172]);
                foreach (var b in initial) region.Seed(b.GetProperty("index").GetInt32(), Bytes(b, "hex"));
                region.ConfigureGeneration(row.GetProperty("generation").GetUInt32());
            }
            var bytes = Bytes(row, "payload_hex"); var saved = bytes.ToArray(); var incoming = Event(op, bytes, 2); Array.Fill(bytes, (byte)0xA5);
            world.Apply(incoming); Check(incoming.Message.Payload.SequenceEqual(saved), "owned payload");
            var calls = row.GetProperty("calls").EnumerateArray().ToArray(); var events = row.GetProperty("events").EnumerateArray().ToArray();
            JsonElement[] Calls(string kind) => calls.Where(c => c.GetProperty("kind").GetString() == kind).ToArray();
            if (op == 0x33)
            {
                var keys = row.GetProperty("keys").EnumerateArray().Select(k => k.GetString()!).Distinct();
                buddy.ConfigureResources(new GameResStore(keys.ToFrozenDictionary(k => SacredResourceHash.Compute(k), k => "<" + k + ">")));
                Check(buddy.Resolve(buddy.Notices.Single()) == Calls("query_notice").Single().GetProperty("text").GetString(), "native query formatting");
                Check(buddy.Notices.Single().NativeChatSelector == 0, "query chat selector"); continue;
            }
            if (op == 0x36)
            {
                var expectedRows = row.GetProperty("buddies").EnumerateArray().ToArray();
                Check(buddy.Buddies.SequenceEqual(expectedRows.Select(Buddy)), "native first-match rows/name preservation/append");
                Check(buddy.Notices.Count == Calls("buddy_notice").Length, "native notification gate"); continue;
            }
            if (op == 0x189)
            {
                var native = row.GetProperty("record_hex");
                Check(region.Pending.Count == (native.ValueKind == JsonValueKind.Null ? 0 : 1), "native region bounds");
                if (native.ValueKind != JsonValueKind.Null) Check(region.Records[((GoldRegionObjectTypesEvent)incoming).RegionIndex].Span.SequenceEqual(Convert.FromHexString(native.GetString()!)), "native record masks/count/append/replace");
                continue;
            }
            var requests = world.CreatureCommands.Interactions.Pending;
            var accepted = op == 0x145 ? Calls("insert").Length != 0 : op is 0x14a or 0x14d or 0x14e ? Calls("create_fx").Length != 0 : events.Length != 0;
            Check(requests.Count == (accepted ? 1 : 0), $"native guard {op:X3} mode {mode}");
            Check(world.CreatureCommands.Interactions.RejectedCount == (accepted ? 0 : 1), "rejection accounting");
            if (accepted)
            {
                var work = requests.Single();
                if (op == 0x145)
                {
                    Check(work.Arguments.SequenceEqual(Calls("insert").Single().GetProperty("args").EnumerateArray().Select(v => v.GetUInt32())), "native insertion arguments");
                    var current = world.FindInventory(2)!; var pairs = Enumerable.Range(0, current.QuestCount).SelectMany(i => current.GetQuestRecord(i).ToArray()).ToArray();
                    Check(pairs.SequenceEqual(Bytes(row, "quest_after_hex")), "native quest-vector append/duplicate/zero-word rules");
                }
                else if (work.FxCreation is { } fx)
                {
                    var cursor = world.Consumers.CreateCursor("native-fx-creation", entry => entry.Value is GoldCreatureInteractionWork { FxCreation: not null });
                    Check(world.Consumers.TryPeek(cursor, out var delivered) && ReferenceEquals(delivered.Value, work), "native accepted creation reaches renderer delivery");
                    var created = Calls("create_fx").Single(); var keyx = Bytes(created, "position");
                    Check(fx.Type == created.GetProperty("type").GetUInt32(), "FX type");
                    Check(fx.Position == new GoldObjectPosition(BitConverter.ToInt32(keyx, 4), BitConverter.ToInt32(keyx, 8), keyx[12], BitConverter.ToUInt16(keyx)), "native FX creation KEYX");
                    var projected = Calls("project");
                    if (projected.Length != 0)
                    {
                        var bits = Bytes(projected.Single(), "bits"); var vector = fx.ProjectionInput!.Value;
                        Check(new[] { BitConverter.SingleToUInt32Bits(vector.X), BitConverter.SingleToUInt32Bits(vector.Y), BitConverter.SingleToUInt32Bits(vector.Z) }
                            .SequenceEqual(Enumerable.Range(0, 3).Select(i => BitConverter.ToUInt32(bits, i * 4))), "native unsigned-to-float projection inputs");
                    }
                    if (events.Length != 0)
                    {
                        var data = Bytes(events[0], "bytes"); Check(fx.EventId == BitConverter.ToUInt32(data, 4), "native FX event id");
                        Check(fx.Parameter == BitConverter.ToUInt32(data, 56), "native FX parameter");
                    }
                }
                else
                {
                    var data = Bytes(events.Single(), "bytes"); Check(work.NativeEventId == BitConverter.ToUInt32(data, 4), "creature event ID");
                    Check(work.Arguments.SequenceEqual(Enumerable.Range(0, work.Arguments.Count).Select(i => BitConverter.ToUInt32(data, 20 + i * 4))), "creature event operands");
                }
            }
            if (op == 0x14e && accepted)
            {
                var reference = ((GoldCreatureFxSpawnEvent)incoming).ObjectReference;
                var initiallyInactive = reference == 2 ? mode == 2 : mode == 5;
                Check(((world.Objects[reference].BaseState.Flags & 0x10) != 0) ==
                    (initiallyInactive || Calls("retire_allowed").Single().GetProperty("value").GetBoolean()), "native participant source retirement/local protection");
            }
        }
        return checks;
    }
}



