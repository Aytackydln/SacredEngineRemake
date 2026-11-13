using System.Text;
using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

internal static class ScriptBitVariableChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-script-bit-variable.json")));
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var context = new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,true);
            if (row.GetProperty("initial").ValueKind == JsonValueKind.Number) context.Variables.Set("TYPE_WEAPON_09",row.GetProperty("initial").GetInt32());
            new WorldScriptExecutor(context).ExecuteStartup(Convert.FromHexString(row.GetProperty("command_hex").GetString()!));
            var expected = Convert.FromHexString(row.GetProperty("record_hex").GetString()!);
            var name = Encoding.Latin1.GetString(expected.AsSpan(0,Array.IndexOf(expected,(byte)0)));
            if (context.Variables.Records.Count != 1 || context.Variables.Records[0].Name != name || context.Variables.Records[0].Value != row.GetProperty("value").GetInt32())
                throw new Exception("Bit-variable state differs from original execution.");
            checks++;
            if (context.VariableGlobals.Mutations.Count != 0 || !context.StartupCompleted) throw new Exception("Bit-variable command invented SetVar global writes.");
            checks++;
        }
        return checks;
    }
}
