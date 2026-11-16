using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.World;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class TriggerBankSourceChecks
{
    public static int Run(string root)
    {
        using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-trigger-bank-source.json")));
        var checks = 0;
        foreach (var row in evidence.RootElement.GetProperty("cases").EnumerateArray())
        {
            var raw = Convert.FromHexString(row.GetProperty("raw_hex").GetString()!);
            var expected = Convert.FromHexString(row.GetProperty("wire_hex").GetString()!);
            // Explicit original-file records are known, including noncanonical
            // Reference words. This does not adopt scratch for a new host gap.
            var registry = new WorldScriptTriggerRegistry(Archive(raw), new());
            Check(registry.WriteBankSection().SequenceEqual(expected), "whole native traversal / ordered record fields");
            var copy = registry.WriteBankSection(); Array.Fill(copy, (byte)0xa5);
            Check(registry.WriteBankSection().SequenceEqual(expected), "returned section owns its bytes");
            if (row.GetProperty("reference").ValueKind == JsonValueKind.Null) continue;
            var reference = row.GetProperty("reference").GetUInt32();
            if (reference == 0) continue; // Native append-zero is not a granted host ID.
            var initialCount = row.GetProperty("initial_count").GetInt32();
            var replica = new GoldWorldReplica();
            replica.Apply(new GoldReferenceRangeEvent(GoldReferenceCodec.CreateTriggerRangeRequest(0, 20, 1) with { Sender = 0 }, reference, 20));
            registry = new(Archive(raw.AsSpan(0, initialCount * 16).ToArray()), replica.TriggerReferences);
            registry.Create(new("Trgnew", 0, 0x8001, 0));
            if (registry.UnknownSlotCount != 0)
            {
                try { registry.WriteBankSection(); throw new Exception("Unknown trigger slots were silently serialized."); }
                catch (InvalidDataException) { checks++; }
            }
            else Check(registry.WriteBankSection().SequenceEqual(expected), "original records plus actual granted creation");
        }
        return checks;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Trigger bank source: " + why); checks++; }
    }
    private static SacredWorldTriggerArchive Archive(byte[] rows)
    {
        var file = new byte[268 + rows.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(file, 0x01475254);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)(rows.Length / 16));
        rows.CopyTo(file, 268); return SacredWorldTriggerArchive.FromPayload(file);
    }
}
