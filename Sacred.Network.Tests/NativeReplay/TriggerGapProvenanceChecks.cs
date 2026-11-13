using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class TriggerGapProvenanceChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-trigger-gap-provenance.json")));
        var checks = 0;
        foreach (var sample in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var count = sample.GetProperty("count").GetInt32();
            var reference = sample.GetProperty("reference").GetUInt32();
            var bytes = new byte[268 + count * 16];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x01475254);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)count);
            for (var i = 0; i < count; i++)
            {
                var row = bytes.AsSpan(268 + i * 16, 16);
                BinaryPrimitives.WriteUInt32LittleEndian(row, (uint)i);
                BinaryPrimitives.WriteUInt16LittleEndian(row[4..], 0x82);
                BinaryPrimitives.WriteUInt32LittleEndian(row[6..], (uint)(9000 + i));
                BinaryPrimitives.WriteUInt16LittleEndian(row[10..], (ushort)(i & 1));
                BinaryPrimitives.WriteUInt32LittleEndian(row[12..], 777);
            }
            var replica = new GoldWorldReplica();
            replica.Apply(new GoldReferenceRangeEvent(GoldReferenceCodec.CreateTriggerRangeRequest((uint)count, 5, 1) with { Sender = 0 }, reference, 5));
            var registry = new WorldScriptTriggerRegistry(SacredWorldTriggerArchive.FromPayload(bytes), replica.TriggerReferences);
            registry.Create(new("Trgnew", 0, 0x8001, 0));
            Check(registry.Count == sample.GetProperty("count_after").GetUInt32(), "native resized vector bound");
            Check(registry.UnknownSlotCount == reference - count && registry.Records.Count == count + 1,
                "scratch-dependent gap slots remain absent from complete host records");
            Check(registry.Records[reference] == new GoldWorldTriggerState(reference, 0x81, 0x8001, 0, 0), "explicit target slot fully initializes");
            var nativeRows = Convert.FromHexString(sample.GetProperty("records_hex").GetString()!);
            var fill = sample.GetProperty("stack_fill").GetByte();
            for (uint slot = (uint)count; slot < reference; slot++)
            {
                Check(!registry.Records.ContainsKey(slot), "no invented Reference for an uninitialized gap");
                var row = nativeRows.AsSpan(checked((int)(slot - count)) * 16, 16);
                Check(row[..4].ToArray().All(value => value == fill) && row[4..].ToArray().All(value => value == 0),
                    "native gap has four scratch Reference bytes and twelve cleared fields");
            }
        }
        return checks;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Native trigger gap: " + why); checks++; }
    }
}
