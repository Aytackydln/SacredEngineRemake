using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class WorldTriggerChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool pass, string message) { if (!pass) throw new Exception("World triggers: " + message); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-world-trigger-transitions.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var original = new GoldWorldTriggerState(17, row.GetProperty("flags").GetUInt16(), row.GetProperty("state").GetUInt16(), 8300, 0);
            var mask = row.GetProperty("mask").GetUInt32();
            var set = row.GetProperty("set").GetBoolean();
            var nativeCallbacks = row.GetProperty("scheduled").GetInt32() != 0;
            var completed = GoldWorldTriggerTransition.TryApply(original, mask, set, out var result);
            Check(completed != nativeCallbacks, "completion matches actual native scheduler calls");
            Check(result == (completed ? original with { State = row.GetProperty("final_state").GetUInt16() } : original), "native state transition or atomic deferred operation");
            var world = new GoldWorldReplica();
            // Slot lookup deliberately differs from the stored Reference field.
            world.Apply(GoldGameEventDecoder.Decode(ScriptResourceChecks.Bank(0x1B1, 6, [original.ToPayload()])));
            var wire = Message(set ? GoldWorldTriggerCodec.SetOpcode : GoldWorldTriggerCodec.ClearOpcode, 0, mask);
            world.Apply(GoldGameEventDecoder.Decode(wire));
            Check(world.WorldTriggers.Records[0] == result && world.WorldTriggers.Pending.Count == (completed ? 0 : 1), "replica uses slot and retains required effects");
        }
        var replica = new GoldWorldReplica();
        replica.Apply(GoldGameEventDecoder.Decode(ScriptResourceChecks.Bank(0x1B1, 6, [new GoldWorldTriggerState(99, 0x84, 0, 8300, 123).ToPayload()])));
        replica.Apply(GoldGameEventDecoder.Decode(Message(0x1B0, 20, 1)));
        Check(replica.WorldTriggers.Pending.Count == 0 && replica.WorldTriggers.Records.Count == 1, "missing state-update slot is ignored");
        replica.Apply(GoldGameEventDecoder.Decode(Message(0x1B0, 0, 1)));
        replica.Apply(GoldGameEventDecoder.Decode(Message(0x1AF, 0, 1)));
        Check(replica.WorldTriggers.Pending.Count == 2 && replica.WorldTriggers.Records[0].State == 0, "inventory-dependent activation and subsequent clear retained in order");
        replica.Apply(GoldGameEventDecoder.Decode(ScriptResourceChecks.Bank(0x1B1, 6, [new GoldWorldTriggerState(99, 0x84, 1, 8300, 0).ToPayload()])));
        Check(replica.WorldTriggers.Pending.Count == 2, "new bank does not claim earlier callbacks executed");
        replica.Clear();
        Check(replica.WorldTriggers.Pending.Count == 0 && replica.WorldTriggers.Records.Count == 0, "disconnect clears pending effects");
        var creation = Message(0x1AE, 100, 0x10084, 0x10001, "Trgtest");
        creation = creation with { Payload = creation.Payload.Concat(new byte[] { 9, 8 }).ToArray() };
        var copy = creation.Payload.ToArray();
        var decoded = (GoldWorldTriggerEvent)GoldGameEventDecoder.Decode(creation);
        creation.Payload.AsSpan().Fill(0xab);
        Check(decoded is { Reference: 100, Value: 0x10084, InitialState: 0x10001, Name: "Trgtest" } && decoded.Message.Payload.SequenceEqual(copy), "creation fields and opaque suffix copied without narrowing");
        replica.Apply(decoded);
        decoded.Message.Payload.AsSpan().Fill(0xcd);
        Check(replica.WorldTriggers.Records.Count == 0 && replica.WorldTriggers.Pending.Single().Message.Payload.SequenceEqual(copy), "creation retained without fabricated allocation/name effects");
        var malformed = new[] { new GoldMessage(0x1AE, 0, 1, 0, new byte[12]), new GoldMessage(0x1AE, 0, 1, 0, new byte[12].Concat(Enumerable.Repeat((byte)65, 65)).ToArray()), Message(0x1AF, 0, 1) with { Payload = new byte[7] } };
        foreach (var message in malformed)
        {
            try { GoldGameEventDecoder.Decode(message); throw new Exception("Invalid trigger packet accepted"); }
            catch (InvalidDataException) { checks++; }
        }
        var batch = new GoldWorldReplica();
        try { GoldGameDispatcher.Dispatch([Message(0x1AE, 0, 1, 0, "name"), malformed[0]], batch.Apply); throw new Exception("Invalid trigger batch accepted"); }
        catch (InvalidDataException) { Check(batch.WorldTriggers.Pending.Count == 0, "malformed late packet prevents partial batch application"); }
        Console.WriteLine($"PASS: {checks} world trigger codec/state/pending-work checks against native transitions.");
        return checks;
    }

    private static GoldMessage Message(ushort opcode, uint reference, uint value, uint state = 0, string? name = null)
    {
        var bytes = new byte[name is null ? 8 : 13 + name.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, reference);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), value);
        if (name is not null)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), state);
            Encoding.Latin1.GetBytes(name).CopyTo(bytes, 12);
        }
        return new(opcode, 0, 1, 0, bytes);
    }
}
