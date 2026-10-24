using System.Text.Json;
using Sacred.Core.Pak.Items;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

internal static class ReceiveActionChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Receive actions: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-receive-actions.json")));
        var names = fixture.RootElement.GetProperty("functions").EnumerateArray().Select(n => n.GetString()!).ToArray();
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var world = new GoldWorldReplica(); var type = row.GetProperty("object_type").GetUInt32();
            var category = row.GetProperty("category").GetByte();
            world.ConfigureObjectCategories(new Dictionary<uint, SacredItemCategory> { [type] = (SacredItemCategory)category });
            world.WorldBanks.Functions.Configure(ReceiveActionPackets.Functions(names));
            var mode = row.GetProperty("mode").GetInt32();
            if (mode != 1) world.Apply(ReceiveActionPackets.Actor(mode, type, row.GetProperty("seed_flags").GetUInt32()));
            var countdowns = world.Session.RegionCountdowns;
            foreach (var value in row.GetProperty("initial_warnings").EnumerateArray())
                world.Apply(Event(0x17e, Words(1, value.GetByte())));
            var warningCount = countdowns.Warnings.Count;
            var opcode = row.GetProperty("opcode").GetUInt16(); var bytes = Bytes(row, "payload_hex"); var original = bytes.ToArray();
            var entry = Event(opcode, bytes); Array.Fill(bytes, (byte)0xa5); world.Apply(entry);
            Check(entry.Message.Payload.SequenceEqual(original), "owned original suffix");
            var calls = row.GetProperty("calls").EnumerateArray().ToArray();
            if (entry is GoldScriptInvokeEvent)
            {
                var request = world.WorldBanks.Invocations.Requests.Single();
                Check(request.TryResolve(out var index), "original configured vector resolves");
                var invokes = calls.Where(c => c[0].GetString() == "invoke").ToArray();
                Check(index == (invokes.Length == 0 ? null : invokes[0][1].GetUInt32()), "native first ASCII-fold match or slot-zero quirk");
                Check(invokes.All(c => c[2].GetUInt32() == 0 && c[3].GetUInt32() == 0), "native receive invocation operands");
            }
            else if (entry is GoldRegionCountdownEvent timer)
            {
                var expected = row.GetProperty("region"); var actual = countdowns.Regions[timer.RegionIndex];
                Check(actual.Remaining == expected[0].GetUInt32(), "native countdown word");
                Check(actual.SelectorWord == expected[1].GetUInt32(), "native full selector retained despite low-byte index");
                Check(actual.WarningIssued == expected[2].GetBoolean(), "one-shot warning latch");
                Check(countdowns.Warnings.Skip(warningCount).Select(w => w.ResourceKey).SequenceEqual(
                    row.GetProperty("keys").EnumerateArray().Select(k => k.GetString()!)), "native threshold and nine-region resource selection");
            }
            else
            {
                var accepted = mode is 0 or 4;
                Check(world.CreatureActions.Requests.Count == (accepted ? 1 : 0), "native active-creature RTTI guard");
                if (!accepted) Check(world.CreatureActions.MissingOrInactiveCount == 1, "rejected work accounted");
                else
                {
                    var request = world.CreatureActions.Requests.Single();
                    Check(request.Object.InstanceVersion == world.Objects[500].InstanceVersion, "receipt target lifetime");
                    if (entry is GoldCreatureMarkerEvent)
                    {
                        var marker = calls.Single(c => c[0].GetString() == "marker"); var data = marker[1];
                        Check(data[0].GetUInt32() == request.MarkerKind && request.MarkerCategory == data[1].GetByte(), "native marker category and mode");
                        Check(data[2].GetUInt32() == 500 && data[3].GetUInt32() == 0 && data[4].GetUInt32() == 0 && data[5].GetUInt32() == 0,
                            "native marker target and operands");
                        Check(marker[2].EnumerateArray().Select(v => v.GetUInt32()).SequenceEqual(new uint[] { 0, 0, 0,
                            BitConverter.SingleToUInt32Bits(request.MarkerScaleX), BitConverter.SingleToUInt32Bits(request.MarkerScaleY),
                            unchecked((uint)request.MarkerOffsetX), unchecked((uint)request.MarkerOffsetY) }),
                            "native UI placement operands");
                    }
                    else if (entry is GoldCreatureTimedControlEvent control)
                    {
                        var fields = row.GetProperty("fields"); var actor = world.Objects[500]; var creature = actor.Creature!;
                        Check(creature.Word200 == fields.GetProperty("0x200").GetUInt32(), "native sleep/wake-suppression OR");
                        Check(creature.Word24D == fields.GetProperty("0x24d").GetUInt32() && creature.Word259 == fields.GetProperty("0x259").GetUInt32(), "native control operands");
                        Check(creature.MovementPathIndex == 0 && creature.MovementPathCount == 0, "native route reset");
                        Check(BitConverter.SingleToUInt32Bits(control.ResetElapsed(12345.5f)) == fields.GetProperty("0x208").GetUInt32(), "native fixed three-unit clock lead");
                        var nativeEvent = Convert.FromHexString(calls.Single(c => c[0].GetString() == "event")[1].GetString()!);
                        Check(BitConverter.ToUInt32(nativeEvent, 4) == control.NativeEventId && BitConverter.ToUInt32(nativeEvent, 8) == control.ClockWord &&
                            BitConverter.ToUInt32(nativeEvent, 0x14) == control.ControlWord, "native event 212 operand ordering");
                    }
                    else if (entry is GoldCreatureTypeChangeEvent change)
                    {
                        if (row.GetProperty("busy").GetBoolean())
                        {
                            var queued = row.GetProperty("queue_after");
                            Check(queued[0].GetUInt32() == request.DeferredScriptCommand && queued[1].GetUInt32() == change.ObjectType, "native deferred command ten and new type");
                        }
                        else Check(row.GetProperty("type_after").GetUInt32() == change.ObjectType, "native requested type operand");
                        Check(world.Objects[500].BaseState.ObjectType == type, "type replacement awaits model/values consumer without inventing its results");
                    }
                }
            }
            world.Clear();
            Check(world.CreatureActions.Requests.Count == 0 && world.WorldBanks.Invocations.Requests.Count == 0 &&
                countdowns.Regions.Count == 0 && countdowns.Warnings.Count == 0, "session cleanup");
        }
        Console.WriteLine($"PASS: {checks} receive-action native-result comparisons."); return checks;
    }
}
