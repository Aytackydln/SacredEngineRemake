using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

internal static class CreatureCommandChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Creature commands: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-commands.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var world = new GoldWorldReplica(); world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Admission()));
            world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Dialogs()));
            world.WorldBanks.Functions.Configure(ReceiveActionPackets.Functions(fixture.RootElement.GetProperty("functions").EnumerateArray().Select(n => n.GetString()!).ToArray()));
            var opcode = row.GetProperty("opcode").GetUInt16(); var mode = row.GetProperty("mode").GetInt32(); var reference = row.GetProperty("parent_ref").GetUInt32();
            var parentKind = opcode == 0x177 ? "fx" : opcode == 0x13c ? "physical" : "creature";
            if (mode == 3) parentKind = opcode == 0x13c ? "fx" : "physical";
            var childKind = mode == 6 ? "fx" : opcode == 0x177 ? "physical" : "creature";
            if (mode != 1) world.Apply(CreatureCommandPackets.Actor(reference, parentKind, mode == 2 ? 0x10u : 0u,
                Bytes(row, "initial_roster_hex"), row.GetProperty("old_dialog").GetUInt32(), row.GetProperty("ki").GetUInt32(), row.GetProperty("option_bits").GetUInt16()));
            if (mode != 4) world.Apply(CreatureCommandPackets.Actor(501, childKind, mode == 5 ? 0x10u : 0u,
                oldDialog: row.GetProperty("old_dialog").GetUInt32(), ki: row.GetProperty("ki").GetUInt32(), optionBits: row.GetProperty("option_bits").GetUInt16()));
            var bytes = Bytes(row, "payload_hex"); var copy = bytes.ToArray(); var entry = Event(opcode, bytes); Array.Fill(bytes, (byte)0xa5);
            world.Apply(entry); var commands = world.CreatureCommands;
            Check(entry.Message.Payload.SequenceEqual(copy), "owned wire and suffix");
            var calls = row.GetProperty("calls").EnumerateArray().ToArray();
            var events = row.GetProperty("events").EnumerateArray().ToArray();
            JsonElement[] Calls(string name) => calls.Where(c => c.GetProperty("kind").GetString() == name).ToArray();
            var expected = opcode switch
            {
                0x114 => true, 0x125 => Calls("value").Length != 0,
                0x12e or 0x12f => mode is 0,
                0x13c or 0x15e or 0x160 => events.Length != 0,
                0x177 => Calls("aim").Length != 0,
                0x180 => Calls("hit_check").Length != 0,
                _ => row.GetProperty("keys").GetArrayLength() != 0
            };
            // Modes 4/5/6 concern only the second endpoint; value and definition receipts remain independent.
            Check(commands.Requests.Count == (expected ? 1 : 0), $"native guard for {opcode:X3}, mode {mode}, reference {reference}");
            Check(commands.ReceivedCount == 1 && commands.RejectedCount == (expected ? 0 : 1), "receipt/rejection counters");
            if (expected)
            {
                var work = commands.Requests.Single();
                if (entry is GoldCreatureDefinitionEvent definition)
                {
                    var native = Calls("definition").Single();
                    Check(definition.Definition.Span.SequenceEqual(Bytes(native, "data")), "348 definition bytes copied unchanged");
                    Check((work.Subject is not null) == (native.GetProperty("context").GetUInt32() != 0), "null and inactive definition context semantics");
                    Check(native.GetProperty("mode").GetUInt32() == 0, "definition uses non-publishing mode");
                }
                else if (entry is GoldCreatureValueCommandEvent value)
                    Check(value.NativeArguments.SequenceEqual(Calls("value").Single().GetProperty("args").EnumerateArray().Select(v => v.GetUInt32())), "native eight-argument value command");
                else if (entry is GoldCreatureLinkEvent)
                {
                    var linked = (GoldCreatureLinkEvent)entry;
                    var child = world.Objects[linked.ChildReference].Creature!;
                    var fields = row.GetProperty(linked.ChildReference == reference ? "parent_fields" : "child_fields");
                    Check(child.Word1F0 == fields.GetProperty("0x1f0").GetUInt32(), "native parent/player AI mode");
                    Check(child.Word1F4 == fields.GetProperty("0x1f4").GetUInt32(), "native behavior masks");
                    Check(child.Word249 == fields.GetProperty("0x249").GetUInt32() && child.Word251 == fields.GetProperty("0x251").GetUInt32(), "native linked control words");
                    Check(world.Objects[reference].LinkedCreatures!.ToPayload().SequenceEqual(Bytes(row, "roster_after_hex")), "native ordered duplicate/append/swap roster");
                }
                else if (entry is GoldPhysicalHitEvent)
                {
                    Check(work.HitNotifications.Count == events.Length, "native primary and optional secondary notification count");
                    for (var i = 0; i < events.Length; i++)
                    {
                        var native = Bytes(events[i], "bytes"); var actual = work.HitNotifications[i];
                        Check(actual.NativeEventId == BitConverter.ToUInt32(native, 4), "native hit event ID");
                        Check(actual.Arguments.SequenceEqual(Enumerable.Range(4, native.Length / 4 - 4).Select(n => BitConverter.ToUInt32(native, n * 4))), "native hit operand copies/halfwords");
                        Check(actual.Object.BaseState.ObjectReference == (events[i].GetProperty("target").GetUInt32() == 0x6030000 ? reference : 501u), "native hit target lifetime");
                    }
                }
                else if (entry is GoldMountTransitionEvent mount)
                {
                    Check(mount.NativeEventId == BitConverter.ToUInt32(Bytes(events[0], "bytes"), 4), "native mount/dismount event choice");
                    Check(world.Objects[reference].Creature!.Word200 == row.GetProperty("parent_fields").GetProperty("0x200").GetUInt32(), "native initiator mount masks");
                    if (world.Objects.TryGetValue(501, out var partner) && partner.Creature is not null)
                    {
                        Check(partner.Creature.Word200 == row.GetProperty("child_fields").GetProperty("0x200").GetUInt32(), "native partner masks including inactive target");
                        Check(partner.Creature.Word2B7 == row.GetProperty("child_word2b7").GetUInt16(), "native partner 2B7 clear");
                    }
                    Check(world.Objects[reference].Creature!.Word2B7 == row.GetProperty("parent_word2b7").GetUInt16(), "native initiator 2B7 clear");
                }
                else if (entry is GoldFxTargetEvent fx)
                {
                    var aim = Calls("aim").Single(); var vector = Bytes(aim, "vector");
                    Check(new[] { fx.Vector.X, fx.Vector.Y, fx.Vector.Z }.SequenceEqual(Enumerable.Range(0, 3).Select(i => BitConverter.ToUInt32(vector, i * 4))), "FX target exact float bits");
                    var particle = Bytes(Calls("particle").Single(), "args");
                    Check(particle.AsSpan(20, 12).SequenceEqual(vector) && BitConverter.ToUInt32(particle, 32) == fx.Parameter &&
                        BitConverter.ToUInt32(particle, 36) == 0x4256a99b, "native particle vector/parameter/scale inputs");
                }
                else if (entry is GoldLocalHitCheckEvent hitCheck)
                    Check(hitCheck.Data.Span.SequenceEqual(Bytes(Calls("hit_check").Single(), "data")), "native local-only 32-byte hit input");
                else if (entry is GoldDialogRegistrationEvent)
                {
                    var registry = commands.Dialogs.Registrations.Single();
                    Check(registry.Key == row.GetProperty("keys")[0].GetString(), "native Talk key formatting");
                    Check(registry.TryResolveFunction(out var function) && function == row.GetProperty("registry_functions")[0].GetUInt32(), "native first matching function or -1");
                    Check(world.Objects[reference].Creature!.DialogIndex == row.GetProperty("parent_fields").GetProperty("0x245").GetUInt32(), "native dialog selector");
                    Check(world.Objects[reference].BaseState.Flags == row.GetProperty("parent_fields").GetProperty("0x14").GetUInt32(), "native interpreter dirty flag");
                    Check(world.ScriptDialogs.Records.OrderBy(p => p.Key).Select(p => p.Value.AttachedObjectReference).SequenceEqual(
                        row.GetProperty("dialog_attachments").EnumerateArray().Select(v => v.GetUInt32())), "native old/new dialog attachment guards");
                    var nativeReply = Calls("publish").Single(); var reply = world.CreatureControls.TakeReply();
                    Check(reply is not null && reply.State.Word245 == nativeReply.GetProperty("dialog").GetUInt32(), "native receipt-time owned callback request");
                    Check(commands.Dialogs.PendingAttachmentCount == 0, "known dialog baseline completes attachments");
                }
            }
            world.Clear(); Check(commands.Requests.Count == 0 && commands.Dialogs.Registrations.Count == 0 && commands.ReceivedCount == 0, "session cleanup");
        }
        Console.WriteLine($"PASS: {checks} creature-command native-result comparisons."); return checks;
    }
}
