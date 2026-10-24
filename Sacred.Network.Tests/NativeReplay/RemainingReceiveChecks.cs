using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Tests.Infrastructure;
using static Sacred.Network.Tests.Infrastructure.RemainingReceivePackets;

internal static class RemainingReceiveChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-remaining-receives.json")));
        var assertions = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Remaining receives: " + why); assertions++; }
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var op = row.GetProperty("opcode").GetUInt16(); var mode = row.GetProperty("mode").GetString();
            var data = Convert.FromHexString(row.GetProperty("payload_hex").GetString()!); var world = new GoldWorldReplica();
            world.Apply(GoldGameEventDecoder.Decode(CreatureCommandPackets.Admission()));
            if (mode != "missing")
            {
                if (mode != "nohero") world.Apply(LobbyLifecyclePackets.Actor(2, mode == "noncreature" ? "physical" : "creature", mode == "inactive" ? 0x10u : 0, owner: 2));
                world.Apply(LobbyLifecyclePackets.Actor(3, owner: 3));
                world.Apply(LobbyLifecyclePackets.Actor(500, owner: 2));
            }
            var calls = row.GetProperty("calls").EnumerateArray().ToArray();
            JsonElement[] Calls(string kind) => calls.Where(c => c.GetProperty("kind").GetString() == kind).ToArray();
            if (op == 0x14B) world.Apply(FxObject());
            if (op == 0x181)
            {
                var basis = new GoldObjectBaseState(3, 1, 0, 3, 300, 400, 1, 0, 0, 0, 3, 0).ToPayload();
                world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16F, 3, basis)));
            }
            if (op == 0x197) world.ReceiveWork.Reconciliation.ConfigureReferences(row.GetProperty("references").EnumerateArray().Select(v => v.GetUInt32()));
            if (op == 0x198)
            {
                world.ReceiveWork.Trade.PanelAvailable = row.GetProperty("panel").GetBoolean();
                if (data.Length > 4) world.Apply(Event(0x198, Words(99, 0x20), 99));
            }
            if (op == 0x199) world.ReceiveWork.PairEffectDistanceFromLocal = _ => row.GetProperty("distance").GetUInt32();
            var incoming = Event(op, data, 2); var before = data.ToArray(); Array.Fill(data, (byte)0xA5); world.Apply(incoming);
            Check(incoming.Message.Payload.SequenceEqual(before), "owned message");
            switch (op)
            {
                case 0x127:
                    Check(world.ReceiveWork.CreatureWork.Count == Calls("event").Length, "XP creature and inactive gates");
                    if (Calls("event").Length != 0)
                    {
                        var args = Calls("event")[0].GetProperty("args").EnumerateArray().Select(v => v.GetUInt32()).ToArray();
                        Check(args[2] == 0x213 && args[7] == unchecked((uint)((GoldExperienceAwardEvent)incoming).Award), "213 signed award operand");
                    }
                    break;
                case 0x132:
                    Check(world.ReceiveWork.StartPositionsReady == row.GetProperty("ready").GetBoolean(), "startup ready latch");
                    Check(world.ReceiveWork.StartPositions!.DecodedData.Span.SequenceEqual(Convert.FromHexString(row.GetProperty("decoded_hex").GetString()!)), "startup buffer");
                    break;
                case 0x14B:
                    var fx = world.Objects[500].Fx!;
                    Check(fx.FxType == row.GetProperty("fx_type").GetUInt32() && fx.Flags == row.GetProperty("fx_flags").GetUInt16(), "FX field copies");
                    Check(fx.References.Count == row.GetProperty("reference_count").GetInt32(), "FX reference vector");
                    Check(((GoldFxPartialEvent)incoming).State.Length == row.GetProperty("consumed").GetInt32(), "partial stream read extent");
                    break;
                case 0x16A:
                    Check(world.Objects[500].BaseState.X == 0 && world.ReceiveWork.ControlledObjects.Count == 1, "controlled full snapshot publication");
                    Check(((GoldControlledObjectEvent)incoming).Snapshot.Message.Opcode == 0x169 && row.GetProperty("normalized").GetUInt16() is 0x169 or 0x16F, "normalization and create fallback");
                    Check((world.ReceiveWork.ControlledObjects.Single().Previous is null) == (mode == "missing"), "receipt-time control context");
                    break;
                case 0x181:
                    var work = world.ReceiveWork.FxWork; var creates = Calls("fx_create"); var events = Calls("fx_event");
                    Check(work.Count == creates.Length && work.Count == events.Length, "batch allocation and final-target gates");
                    for (var i = 0; i < work.Count; i++)
                    {
                        var w = work[i]; var args = events[i].GetProperty("args").EnumerateArray().Select(v => v.GetUInt32()).ToArray();
                        Check(w.FxType == creates[i].GetProperty("fx_type").GetUInt32() && w.CreationParameter == creates[i].GetProperty("parameter").GetUInt32(), "batch factory inputs");
                        var keyx = Convert.FromHexString(creates[i].GetProperty("position_hex").GetString()!);
                        Check(w.Position.Sector == BinaryPrimitives.ReadUInt16LittleEndian(keyx) &&
                            w.Position.X == BinaryPrimitives.ReadInt32LittleEndian(keyx.AsSpan(4)) &&
                            w.Position.Y == BinaryPrimitives.ReadInt32LittleEndian(keyx.AsSpan(8)) && w.Position.Surface == keyx[12], "native KEYX field ordering");
                        Check(w.EventId == args[0] && w.SourceReference == args[1] && w.TargetReference == args[2], "batch event context");
                        Check(unchecked((uint)w.Parameter) == args[13] && (uint)w.Index == args[14], "batch scalar cap/truncation and index");
                        Check(BitConverter.SingleToUInt32Bits(w.Vector1.X) == args[7] && BitConverter.SingleToUInt32Bits(w.Vector2.X) == args[10], "batch vector selection");
                    }
                    break;
                case 0x197:
                    var reply = world.ReceiveWork.Reconciliation.Replies.Single();
                    Check(reply.Payload.Length == Calls("send").Single().GetProperty("size").GetInt32(), "reconciliation array capacity");
                    Check(Convert.ToHexStringLower(SHA256.HashData(reply.Payload)) == Calls("send").Single().GetProperty("sha256").GetString(), "complete native reconciliation bytes");
                    break;
                case 0x198:
                    var t = world.ReceiveWork.Trade;
                    Check(t.ActivePartner == row.GetProperty("partner").GetUInt32(), "trade partner gate");
                    Check(t.Amount == row.GetProperty("amount").GetUInt32(), "trade amount");
                    var flags = new[] { t.FlagBB4, t.FlagBB5, t.FlagBB6, t.FlagBB7, t.FlagBB8 }.Select(v => v ? (byte)1 : (byte)0).ToArray();
                    Check(flags.SequenceEqual(Convert.FromHexString(row.GetProperty("flags_hex").GetString()!)), "original trade flag setters");
                    break;
                case 0x199:
                    Check(world.ReceiveWork.CreatureWork.Count == Calls("pair_model_boundary").Length, "paired effect pre-model gates"); break;
                case 0x19B:
                    Check(world.ReceiveWork.CreatureWork.Count == Calls("creature_command").Length, "command recipient cast including inactive"); break;
                case 0x1AA:
                    Check(world.ReceiveWork.Interpreter!.State.Span.SequenceEqual(Convert.FromHexString(row.GetProperty("state_hex").GetString()!)), "interpreter prefix copy"); break;
                case 0x1AB:
                    Check(world.ReceiveWork.CreatureTable!.Message.Payload.SequenceEqual(Convert.FromHexString(Calls("disk_write").Single().GetProperty("hex").GetString()!)), "received table bytes");
                    Check(Calls("creature_reload").Length == 1 && Calls("creature_refresh").Length == 1, "native reload and refresh boundaries"); break;
                case 0x1AC:
                    Check(world.ReceiveWork.Banner!.ClearsBanner == (Calls("banner").Length == 1), "empty banner reset");
                    Check(Calls("configuration").Length == 1, "banner configuration notification boundary"); break;
            }
        }
        return assertions;
    }
}
