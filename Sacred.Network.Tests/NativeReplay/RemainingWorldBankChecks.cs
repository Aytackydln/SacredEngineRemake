using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class RemainingWorldBankChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool pass, string name) { if (!pass) throw new Exception(name); checks++; }
        void Reject(Action action, string name)
        {
            try { action(); } catch (InvalidDataException) { checks++; return; }
            throw new Exception("Accepted invalid remaining bank: " + name);
        }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-remaining-world-banks.json")));
        var messages = new List<GoldMessage>();
        foreach (var sample in fixture.RootElement.GetProperty("samples").EnumerateArray())
        {
            var opcode = sample.GetProperty("opcode").GetUInt16();
            var bytes = Convert.FromHexString(sample.GetProperty("payload_hex").GetString()!);
            var snapshot = GoldWorldBankSnapshot.FromPayload(opcode, bytes);
            Check(snapshot.ToPayload().SequenceEqual(bytes) && snapshot.RemainingData.IsEmpty, "remaining bank lossless consumed snapshot");
            Check(snapshot.Sections.Count == sample.GetProperty("sections").GetArrayLength(), "native section count");
            var cursor = 40;
            if (snapshot.Globals is { } globals)
            {
                var trace = sample.GetProperty("global_native_reads").EnumerateArray().ToArray();
                Check(globals.FirstBlock.SequenceEqual(bytes.AsSpan(cursor + trace[0].GetProperty("offset").GetInt32(), trace[0].GetProperty("bytes").GetInt32())), "native first global destination");
                Check(globals.SecondBlock.SequenceEqual(bytes.AsSpan(cursor + trace[1].GetProperty("offset").GetInt32(), trace[1].GetProperty("bytes").GetInt32())), "native second global destination");
                Check(globals.Table.SequenceEqual(bytes.AsSpan(cursor + trace[2].GetProperty("offset").GetInt32(), trace[2].GetProperty("bytes").GetInt32())), "native uncounted table destination");
                for (var index = 0; index < 256; index++)
                    Check(globals.GetTableRecord(index).SequenceEqual(globals.Table.Slice(index * 24, 24)), "all native global table entries preserved");
                cursor += trace.Sum(read => read.GetProperty("bytes").GetInt32());
            }
            else Check(sample.GetProperty("global_native_reads").GetArrayLength() == 0, "bank 4 has no invented global preface");
            foreach (var section in sample.GetProperty("sections").EnumerateArray())
            {
                var index = section.GetProperty("index").GetInt32();
                var actual = snapshot.Sections[index];
                var length = section.GetProperty("bytes").GetInt32();
                Check(actual.Count == section.GetProperty("count").GetInt32() && actual.ElementSize == section.GetProperty("element_size").GetInt32(), "native count/stride parity");
                Check(section.GetProperty("native_reads").EnumerateArray().Sum(read => read.GetProperty("bytes").GetInt32()) == length, "native read segments account for complete section");
                if (actual.Kind == GoldWorldBankSectionKind.FixedRecords)
                    Check(actual.Records.SelectMany(record => record.Data.ToArray()).SequenceEqual(bytes.AsSpan(cursor, length).ToArray()), "fixed serialized bytes equal native read source");
                else
                {
                    var recordIndex = 0;
                    foreach (var expected in section.GetProperty("records").EnumerateArray())
                    {
                        var record = actual.Records[recordIndex++];
                        Check(record.Prefix.SequenceEqual(Convert.FromHexString(expected.GetProperty("prefix_hex").GetString()!)), "composite/keyed fixed prefix preserved");
                        if (expected.TryGetProperty("data_hex", out var data))
                            Check(record.Data.SequenceEqual(Convert.FromHexString(data.GetString()!)), "keyed vector elements preserve native record size");
                        if (expected.TryGetProperty("vectors_hex", out var vectors))
                        {
                            Check(record.Vectors.Count == vectors.GetArrayLength(), "native composite vector count");
                            for (var v = 0; v < record.Vectors.Count; v++)
                                Check(record.Vectors[v].Data.SequenceEqual(Convert.FromHexString(vectors[v].GetString()!)), "native composite vector destination bytes");
                        }
                        if (expected.TryGetProperty("children", out var children))
                        {
                            Check(record.ChildRecords.Count == children.GetArrayLength(), "nested child count");
                            for (var c = 0; c < record.ChildRecords.Count; c++)
                            {
                                var child = record.ChildRecords[c];
                                Check(child.Data.SequenceEqual(Convert.FromHexString(children[c].GetProperty("data_hex").GetString()!)), "24 native fixed child bytes retained");
                                Check(child.Vectors.Count == 1 && child.Vectors[0].Data.SequenceEqual(Convert.FromHexString(children[c].GetProperty("vectors_hex")[0].GetString()!)), "child word vector precedes fixed fields on wire");
                            }
                        }
                    }
                    Check(recordIndex == actual.Count, "every native variable record represented");
                }
                cursor += length;
            }
            Check(cursor == bytes.Length, "native stream has no unexplained bytes");
            var message = GoldWorldBankCodec.Create(1, 1, snapshot);
            messages.Add(message);
            Check(GoldGameEventDecoder.Decode(message) is GoldWorldBankEvent && message.Payload.SequenceEqual(bytes), "remaining bank production decoder/producer path");
            // Includes all global preface, nested count/data boundaries and final scalar reads.
            for (var length = 0; length < bytes.Length; length++)
                Reject(() => GoldWorldBankSnapshot.FromPayload(opcode, bytes.AsSpan(0, length)), "every remaining bank truncation");
            for (var index = 0; index < snapshot.Sections.Count; index++)
            {
                var inflated = bytes.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(inflated.AsSpan(index * 4), uint.MaxValue);
                Reject(() => GoldWorldBankSnapshot.FromPayload(opcode, inflated), "inflated used header count");
            }
            var source = bytes.ToArray(); bytes.AsSpan().Clear(); snapshot.ToPayload().AsSpan().Clear();
            Check(snapshot.ToPayload().SequenceEqual(source), "remaining bank snapshot isolation");
            var suffix = source.Concat(new byte[] { 0x1A, 0xFF }).ToArray();
            Check(GoldWorldBankSnapshot.FromPayload(opcode, suffix).RemainingData.SequenceEqual(new byte[] { 0x1A, 0xFF }), "unread suffix preserved in remaining banks");
            if (opcode == 0x1B1)
                Check(BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Sections[6].Records[1].Data) == 0x12345678, "native unchecked marker preserved without invented validation");
            else
                Check(snapshot.GetHeaderWord(9) == 0xA55A5AA5, "bank 4 word 9 retained without treating it as a count");
        }

        var empty1 = new byte[40 + GoldWorldBankGlobals.Size];
        Check(GoldWorldBankSnapshot.FromPayload(0x1B1, empty1).Sections.All(section => section.Count == 0), "zero-count bank 1 still consumes mandatory globals");
        Reject(() => GoldWorldBankSnapshot.FromPayload(0x1B1, new byte[40]), "bank 1 cannot omit globals");
        Check(GoldWorldBankSnapshot.FromPayload(0x1B4, new byte[40]).Sections.All(section => section.Count == 0), "zero-count bank 4");

        // Nested bank 4 record with empty vectors and zero children; mutate each inner count separately.
        var minimal = new byte[40 + 88 + 16];
        BinaryPrimitives.WriteUInt32LittleEndian(minimal.AsSpan(4), 1);
        var state = GoldWorldBankSnapshot.FromPayload(0x1B4, minimal);
        Check(state.Sections[1].Records[0].Vectors.Count == 3 && state.Sections[1].Records[0].ChildRecords.Count == 0, "empty composite keeps all three vector slots");
        foreach (var offset in new[] { 128, 132, 136, 140 })
        {
            var inflated = minimal.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(inflated.AsSpan(offset), uint.MaxValue);
            Reject(() => GoldWorldBankSnapshot.FromPayload(0x1B4, inflated), "inflated nested/composite vector count");
        }
        // A nested child whose vector consumes the required fixed suffix must fail.
        var childOverrun = new byte[40 + 88 + 16 + 4 + 24];
        BinaryPrimitives.WriteUInt32LittleEndian(childOverrun.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(childOverrun.AsSpan(140), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(childOverrun.AsSpan(144), 6);
        Reject(() => GoldWorldBankSnapshot.FromPayload(0x1B4, childOverrun), "child fixed fields exhausted by vector");

        // Uncaptured section 5 with no vector elements: no special case inferred from capture absence.
        var minimal5 = new byte[40 + 24 + 8]; BinaryPrimitives.WriteUInt32LittleEndian(minimal5.AsSpan(20), 1);
        var section5 = GoldWorldBankSnapshot.FromPayload(0x1B4, minimal5).Sections[5].Records[0];
        Check(section5.Vectors.Count == 2 && section5.Vectors[0].ElementSize == 44 && section5.Vectors.All(vector => vector.Count == 0), "uncaptured section 5 supports empty native vectors");
        foreach (var offset in new[] { 64, 68 })
        {
            var inflated = minimal5.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(inflated.AsSpan(offset), uint.MaxValue);
            Reject(() => GoldWorldBankSnapshot.FromPayload(0x1B4, inflated), "inflated uncaptured section vector count");
        }
        var world = new GoldWorldReplica();
        foreach (var message in messages) world.Apply(GoldGameEventDecoder.Decode(message));
        Check(world.WorldBanks.States.Count == 2 && world.LocalPlayerSlot == 0 && world.Objects.Count == 0, "remaining banks retained without invented world startup");
        var delivered = 0;
        Reject(() => GoldGameDispatcher.Dispatch([messages[0], messages[1] with { Payload = childOverrun }], _ => delivered++), "malformed nested bank in late batch");
        Check(delivered == 0, "nested dispatch validation is atomic");
        world.Clear(); Check(world.WorldBanks.States.Count == 0, "remaining bank disconnect cleanup");
        File.WriteAllText(Path.Combine(root, "docs/game-server/evidence/remaining-world-banks-managed.json"), JsonSerializer.Serialize(new
        {
            checks, native_connection = false, synthetic_nesting = true,
            limitations = "Native read parity, bounds and replica checks only; scripts, host world creation and owned reference mapping remain pending."
        }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine($"PASS: {checks} remaining world-bank checks; includes uncaptured nested records");
        return checks;
    }
}
