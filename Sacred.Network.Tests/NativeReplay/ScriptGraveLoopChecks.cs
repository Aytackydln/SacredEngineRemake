using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

internal static class ScriptGraveLoopChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("Grave loop: " + why); checks++; }
        using var predicates = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-script-control-flow.json")));
        foreach (var row in predicates.RootElement.GetProperty("predicates").EnumerateArray())
        {
            var variables = new WorldScriptVariables();
            if (row.GetProperty("variable_hex").ValueKind == JsonValueKind.String)
            {
                var bytes = Convert.FromHexString(row.GetProperty("variable_hex").GetString()!);
                variables.Set(Encoding.Latin1.GetString(bytes.AsSpan(0, Array.IndexOf(bytes, (byte)0))), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(32)));
            }
            var command = new SacredScriptCommand(0, Convert.FromHexString(row.GetProperty("command_hex").GetString()!));
            Check(SacredScriptConditionReader.Evaluate(command, row.GetProperty("multiplayer").GetBoolean(), variables.Find) == row.GetProperty("native_result").GetBoolean(), "actual predicate");
        }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-script-grave-loop.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var code = Convert.FromHexString(row.GetProperty("code_hex").GetString()!);
            var visited = new List<int>();
            var context = new WorldScriptExecutionContext(new(), [new("reserved", 0, 0, -1, 0, 0), new("loop", 0, code.Length, -1, 1, 0)], code, false)
            { BeforeCommand = command => visited.Add(command.FileOffset) };
            context.Variables.Set("grvarNr", row.GetProperty("initial").GetInt32());
            new WorldScriptExecutor(context).Invoke(1);
            var records = Convert.FromHexString(row.GetProperty("records_hex").GetString()!);
            Check(context.Variables.Records.Count == records.Length / 36, "record count");
            for (var index = 0; index < context.Variables.Records.Count; index++)
            {
                var record = records.AsSpan(index * 36, 36);
                var end = record[..32].IndexOf((byte)0);
                Check(context.Variables.Records[index].Name == Encoding.Latin1.GetString(record[..end]), "ordered name");
                Check(context.Variables.Records[index].Value == BinaryPrimitives.ReadInt32LittleEndian(record[32..]), "value");
            }
            // Native traces include predicates and branch-end dispatch; BeforeCommand observes effects only.
            var expected = row.GetProperty("visited_offsets").EnumerateArray().Select(value => value.GetInt32())
                .Where(offset => offset != 0 && offset != 80).ToArray();
            Check(visited.SequenceEqual(expected), "effect/restart order");
            if (row.GetProperty("initial").GetInt32() == 0)
            {
                var bounded = new WorldScriptExecutionContext(new(), context.Functions, code, false);
                bounded.Variables.Set("grvarNr", 0);
                try { new WorldScriptExecutor(bounded, instructionBudget: 5).Invoke(1); Check(false, "budget must stop restarted execution"); }
                catch (WorldScriptExecutionFailure failure)
                { Check(bounded.IsInvalidated && failure.Message.Contains("budget"), "restart preserves shared budget and failure invalidation"); }
            }
        }
        foreach (var row in fixture.RootElement.GetProperty("expansions").EnumerateArray())
        {
            var variables = new WorldScriptVariables(); variables.Set("counter", row.GetProperty("value").GetInt32());
            Check(WorldScriptVariableNames.Expand(row.GetProperty("text").GetString()!, variables.Find) == row.GetProperty("result").GetString(), "name expansion");
        }
        return checks;
    }
}
