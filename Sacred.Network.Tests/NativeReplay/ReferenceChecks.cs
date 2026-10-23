using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class ReferenceChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
        void Reject(Action action, string name)
        {
            try { action(); } catch (InvalidDataException) { checks++; return; }
            throw new Exception("Accepted invalid reference data: " + name);
        }
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-reference-allocation.json")));
        foreach (var sample in native.RootElement.GetProperty("request_guards").EnumerateArray())
        {
            var count = sample.GetProperty("count").GetUInt32();
            var slot = (byte)sample.GetProperty("player_slot").GetUInt32();
            if (sample.GetProperty("native_accepted").GetBoolean())
            {
                var message = GoldReferenceCodec.CreateRequest(count, slot);
                Check(message.Payload.Length == 4 && message.Sender == slot && message.Origin == slot &&
                    GoldGameEventDecoder.Decode(message) is GoldReferenceRequestEvent request && request.Count == count,
                    "native request guard and scalar request shape");
            }
            else
            {
                try { GoldReferenceCodec.CreateRequest(count, slot); throw new Exception("Native-rejected request accepted"); }
                catch (ArgumentOutOfRangeException) { checks++; }
            }
        }
        byte[] Words(params uint[] values)
        {
            var bytes = new byte[values.Length * 4];
            for (var index = 0; index < values.Length; index++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), values[index]);
            return bytes;
        }
        foreach (var sample in native.RootElement.GetProperty("reference_guards").EnumerateArray())
        {
            var value = sample.GetProperty("value").GetUInt32();
            if (sample.GetProperty("native_in_range").GetBoolean())
                Check(GoldReferenceBatch.FromPayload(Words(value)).References[0] == value, "native allocation ID boundary");
            else Reject(() => GoldReferenceBatch.FromPayload(Words(value)), "native allocation ID boundary");
        }
        foreach (var sample in native.RootElement.GetProperty("range_copies").EnumerateArray())
        {
            var message = new GoldMessage(0x154, 0, 0, 4, Words(sample.GetProperty("start").GetUInt32(), sample.GetProperty("count").GetUInt32()));
            Check(GoldGameEventDecoder.Decode(message) is GoldReferenceRangeEvent range &&
                range.EndExclusive == sample.GetProperty("native_end_exclusive").GetUInt32(), "native range bound arithmetic");
        }
        foreach (var size in new[] { 1, 2, 3, 5, 7 })
            Reject(() => GoldReferenceBatch.FromPayload(new byte[size]), "partial allocation word");
        Reject(() => GoldReferenceBatch.FromPayload(new byte[GoldReferenceCodec.ReferenceLimit * 4]), "native allocation count limit");
        var source = Words(17, 18, 19);
        var copied = GoldReferenceBatch.FromPayload(source); source[0] = 0;
        Check(copied.Count == 3 && copied.References.SequenceEqual(new uint[] { 17, 18, 19 }), "allocated IDs isolated from caller changes");
        Check(GoldReferenceBatch.FromPayload([]).Count == 0, "empty native allocation does not invent an ID");
        var batchMessage = new GoldMessage(0x153, 0, 0, 4, Words(17, 18, 19));
        Check(GoldGameEventDecoder.Decode(batchMessage) is GoldReferenceBatchEvent { Batch.Count: 3 }, "server allocation is distinct from client scalar request");
        var approval = new GoldMessage(0x11, 0, 0, 1, [4, 2, 0, 0, 0, 0]);
        var tracker = new GoldAdmissionTracker(); tracker.Begin();
        Reject(() => tracker.ProcessBatch([batchMessage]), "allocation before admission");
        Reject(() => tracker.ProcessBatch([approval, batchMessage with { Origin = 2 }]), "forged allocation origin");
        Reject(() => tracker.ProcessBatch([approval, batchMessage with { Payload = Words(17, 0) }]), "invalid ID after valid approval");
        Check(tracker.Status.Phase == GoldAdmissionPhase.WaitingForResponse, "invalid allocation leaves the whole batch uncommitted");
        tracker.ProcessBatch([approval, batchMessage]);
        Check(tracker.Status.PlayerSlot == 2, "native allocation in an admitted startup batch");
        Console.WriteLine($"PASS: {checks} native reference-allocation checks");
        return checks;
    }
}
