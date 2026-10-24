using System.Numerics;
using System.Text.Json;
using Sacred.Network.Gameplay;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

internal static class SpecialFxReceiveChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Special FX receive: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-special-fx-receives.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var world = new GoldWorldReplica();
            world.SpecialFx.ConfigureSectors(MovementTriggerInsertionChecks.Index([(1u, 0, 0)]));
            var mode = row.GetProperty("mode").GetInt32();
            if (mode != 1)
            {
                if (mode == 2) world.Apply(Actor(physicalOnly: true));
                else world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16f, 1, Bytes(row, "before_hex"))));
            }
            var opcode = row.GetProperty("opcode").GetUInt16(); var wire = Bytes(row, "payload_hex"); var source = wire.ToArray();
            var entry = (GoldSpecialFxEvent)Event(opcode, wire); Array.Fill(wire, (byte)0xcc); world.Apply(entry);
            Check(entry.Message.Payload.SequenceEqual(source), "owned exact float operands");
            var accepted = mode == 0 && (row.GetProperty("flags").GetUInt32() & 0x10) == 0;
            Check(world.SpecialFx.AppliedCount == (accepted ? 1 : 0), "native type/missing/inactive guard");
            if (mode == 0)
                Check(world.Objects[500].Fx!.ToPayload().SequenceEqual(Bytes(row, "after_hex")), "native FX fields, signed position, surface and KEYX sector result");
            if (accepted)
            {
                var obj = world.Objects[500]; var state = obj.SpecialFx!;
                Check(state.Version == 1 && world.SpecialFx.PendingRelocationCount == 0, "one instance-scoped update and configured sector lookup");
                if (entry is GoldPingPongBallEvent ball)
                {
                    var values = row.GetProperty("particle_position_hex").GetString()!;
                    Check(VectorBytes(state.ParticlePosition!.Value).SequenceEqual(Convert.FromHexString(values)), "native particle position words");
                    Check(state.ParticleClockWord == row.GetProperty("particle_clock").GetUInt32(), "original x87 Int64 truncation low word including NaN/overflow");
                    Check(state.ParticleFlags == (ball.SetsParticleFlag ? row.GetProperty("particle_flags").GetUInt16() : null), "false flag preserves unknown particle flags");
                }
                if (entry is GoldPacmanGhostEvent ghost)
                {
                    var input = Convert.FromHexString(row.GetProperty("projection_inputs")[0].GetString()!);
                    var expected = new Vector3(BitConverter.ToSingle(input), BitConverter.ToSingle(input, 4), BitConverter.ToSingle(input, 8));
                    Check(state.ProjectedPositionInput == expected, "native signed integer-to-float projection input");
                    Check(state.ParticlePosition is null, "viewport projection result is not guessed");
                }
            }
            world.Clear(); Check(world.SpecialFx.AppliedCount == 0 && world.SpecialFx.PendingRelocationCount == 0, "special FX session cleanup");
        }
        Console.WriteLine($"PASS: {checks} special FX receive native-result comparisons."); return checks;
    }
    private static byte[] VectorBytes(GoldFxVectorBits vector) => Words(vector.X, vector.Y, vector.Z);
}
