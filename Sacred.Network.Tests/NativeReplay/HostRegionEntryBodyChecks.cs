using System.Text;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class HostRegionEntryBodyChecks
{
    public static int Run(string root)
    {
        using var proof = HostSectorInitializationChecks.Read(root, "native-host-region-entry-body");
        var checks = 0;
        foreach (var row in proof.RootElement.GetProperty("cases").EnumerateArray())
        {
            var context = Execute(proof.RootElement, row);
            Check(context.Variables.Records.SelectMany(value => value.ToPayload()).SequenceEqual(Bytes(row, "after_hex")),
                "all native variable bytes, including unchanged names");
            var publications = context.Variables.Publications;
            var native = row.GetProperty("notifications").EnumerateArray().ToArray();
            Check(publications.Count == native.Length, "native notification count");
            for (var index = 0; index < native.Length; index++)
                Check(publications[index].Opcode == native[index].GetProperty("opcode").GetUInt16() &&
                    publications[index].Payload.SequenceEqual(Bytes(native[index], "payload_hex")), "native indexed variable publication");
            // Native visited includes branch-control instructions skipped by the
            // managed flat branch reader. Compare effectful opcodes separately.
            Check(context.Coverage.GetValueOrDefault((ushort)69) == row.GetProperty("visited").EnumerateArray().Count(value => value.GetUInt16() == 69),
                "native clear-bit execution gate");
            Check(!context.IsInvalidated && context.CurrentFunctionQuest is null && context.Triggers.Count == 0,
                "dispatcher cleanup and no excluded-callee effects");
            Check(context.VariableGlobals.Mutations.Count == 0, "native fixed variable globals stay unchanged");
        }
        return checks;
        void Check(bool value, string reason) { if (!value) throw new Exception("Host Region1Enter: " + reason); checks++; }
    }

    internal static byte[] WireExample(string root)
    {
        using var proof = HostSectorInitializationChecks.Read(root, "native-host-region-entry-body");
        var row = proof.RootElement.GetProperty("cases").EnumerateArray().First(value => value.GetProperty("notifications").GetArrayLength() > 0);
        return Execute(proof.RootElement, row).Variables.Publications.Single().Payload;
    }

    private static WorldScriptExecutionContext Execute(JsonElement proof, JsonElement row)
    {
        var index = proof.GetProperty("function_index").GetInt32();
        var excluded = proof.GetProperty("excluded_function_index").GetInt32();
        var functions = Enumerable.Range(0, Math.Max(index, excluded) + 1)
            .Select(_ => new SacredScriptFunction("reserved", 0, 0, -1, 0, 0)).ToArray();
        var code = Bytes(proof, "code_hex");
        functions[index] = new(Name("definition_hex"), 0, code.Length, -1, 0, 0);
        functions[excluded] = new(Name("excluded_definition_hex"), 0, 0, -1, 0, 0);
        var context = new WorldScriptExecutionContext(new(), functions, code, true) { LocalPlayerSlot = 1 };
        var before = Bytes(row, "before_hex");
        context.Variables.Import(Enumerable.Range(0, before.Length / 36).Select(slot => GoldScriptVariableRecord.FromPayload(before.AsSpan(slot * 36, 36))));
        new WorldScriptExecutor(context).ExecuteStartup(ReadOnlyMemory<byte>.Empty);
        context.IsEnteringRegion = true;
        try { new WorldScriptExecutor(context).Invoke(index); }
        finally { context.IsEnteringRegion = false; }
        return context;
        string Name(string field) => Encoding.Latin1.GetString(Bytes(proof, field).AsSpan(0, 64)).TrimEnd('\0');
    }

    private static byte[] Bytes(JsonElement row, string name) => HostSectorInitializationChecks.Bytes(row, name);
}
