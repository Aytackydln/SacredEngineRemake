using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

internal static class DialogAssociationNormalizationChecks
{
    public static byte[] BankThreeWireExample(string root)
    {
        using var fixture = Read(root);
        var context = Construct(fixture.RootElement.GetProperty("cases")[0]);
        var body = context.DialogAssociations.WriteNormalizedBankSection();
        // Focused one-section transport fixture, not a generated host world bank.
        var payload = new byte[40 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(5 * 4), (uint)context.DialogAssociations.Records.Count);
        body.CopyTo(payload, 40);
        return payload;
    }
    public static int Run(string root)
    {
        using var fixture = Read(root);
        var checks = 0;
        Check(Enumerable.Range(0,88).Count(SacredScriptDialogAssociationLayout.IsConstructorWrittenByte) == 14, "14 constructed bytes and 74 inherited bytes");
        foreach (var sample in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var context = Construct(sample);
            var row = context.DialogAssociations.Records.Single();
            var raw = Bytes(sample, "raw_hex"); var canonical = Bytes(sample, "canonical_hex");
            Check(!row.HasCompleteBytes, "normalization does not invent raw provenance");
            try { row.ToPayload(); throw new Exception("Unknown raw bytes serialized"); } catch (InvalidDataException) { checks++; }
            Compare(row.ToNormalizedPayload(), canonical, "explicit canonical row");
            Compare(context.DialogAssociations.WriteNormalizedBankSection(), Bytes(sample, "canonical_wire_hex"), "original bank-3 section-5 writer after declared normalization");
            for (var i = 0; i < 88; i++) Check(canonical[i] == (SacredScriptDialogAssociationLayout.IsConstructorWrittenByte(i) ? raw[i] : 0), "only unwritten bytes normalized");
            var imported = WorldScriptDialogAssociation.FromPayload(raw);
            Compare(imported.ToNormalizedPayload(), canonical, "same explicit normalization for imported rows");
            Compare(imported.ToPayload(), raw, "imported raw bytes stay unchanged");
            Compare(raw, Bytes(sample, "wire_hex"), "original unnormalized writer copies every byte");
            Lookup(context.DialogAssociations, sample);
        }
        foreach (var sample in fixture.RootElement.GetProperty("lookups").EnumerateArray())
        {
            var table = new WorldScriptDialogAssociations();
            table.Import(Bytes(sample, "raw_hex").Chunk(88).Select(bytes => WorldScriptDialogAssociation.FromPayload(bytes)));
            Lookup(table, sample);
            Compare(table.WriteNormalizedBankSection(), Bytes(sample, "canonical_hex"), "ordered multirow normalization");
        }
        var authored = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true);
        authored.Registrations.Import(Bytes(fixture.RootElement.GetProperty("installed_input"), "registrations_hex")
            .Chunk(80).Select(bytes => SacredScriptDialog.FromPayload(bytes)));
        new WorldScriptExecutor(authored).ExecuteStartup(Bytes(fixture.RootElement, "installed_commands_hex"));
        Check(authored.DialogAssociations.Records.Count == 128, "complete authored association count");
        Check(authored.DialogAssociations.Records.All(row => !row.HasCompleteBytes), "authored normalization retains raw provenance gaps");
        foreach (var native in fixture.RootElement.GetProperty("installed").EnumerateArray())
            Compare(authored.DialogAssociations.WriteNormalizedBankSection(), Bytes(native, "canonical_wire_hex"), "all 128 original writer rows after declared normalization");
        return checks;
        void Lookup(WorldScriptDialogAssociations table, JsonElement sample)
        {
            var result = table.FindRegistrationSlot(sample.GetProperty("type").GetUInt32(), sample.GetProperty("qualifier").GetUInt16());
            var expected = sample.GetProperty("lookup");
            Check(result.HasValue == expected.GetProperty("matched").GetBoolean(), "no match versus matched zero or negative slot");
            if (result.HasValue) Check(result.Value == expected.GetProperty("slot").GetInt32(), "native ordered type/qualifier lookup");
            foreach (var value in expected.GetProperty("read_offsets").EnumerateArray())
                Check(value.GetInt32() is >= 0 and < 4 or >= 80 and < 86, "native lookup read footprint");
        }
        void Compare(byte[] actual, byte[] expected, string why) => Check(actual.AsSpan().SequenceEqual(expected), why);
        void Check(bool value, string why) { if (!value) throw new Exception("Dialog normalization: " + why); checks++; }
    }
    private static JsonDocument Read(string root) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-dialog-association-normalization.json")));
    private static byte[] Bytes(JsonElement row, string field) => Convert.FromHexString(row.GetProperty(field).GetString()!);
    private static WorldScriptExecutionContext Construct(JsonElement sample)
    {
        var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true);
        context.Registrations.Import(Bytes(sample, "registrations_hex").Chunk(80).Select(bytes => SacredScriptDialog.FromPayload(bytes)));
        new WorldScriptExecutor(context).ExecuteStartup(Bytes(sample, "command_hex"));
        return context;
    }
}
