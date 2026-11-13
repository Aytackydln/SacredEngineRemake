using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class ScriptMouseoverScratchChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-script-mouseover-scratch.json")));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var mouseovers = new WorldScriptMouseovers(_ => throw new Exception("Unexpected resource lookup"), (_, _) => { });
            var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true) { Mouseovers = mouseovers };
            new WorldScriptExecutor(context).ExecuteStartup(Convert.FromHexString(row.GetProperty("code_hex").GetString()!));
            Check(context.StartupCompleted, "native sequence completion");
            Check(context.VariableGlobals.UnresolvedIndexCount == row.GetProperty("unresolved").GetInt32(), "first-byte provenance versus numeric suffix");
            var storage = row.GetProperty("name_storage").EnumerateArray().Select(value => Convert.FromHexString(value.GetString()!)).ToArray();
            var actual = context.VariableGlobals.ApplyWithNameStorage(new int[150], (index, _) => storage[index]);
            var expected = row.GetProperty("globals").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            for (var i = 0; i < expected.Length; i++) Check(actual[i] == expected[i], $"native global word {i}");
            Check(mouseovers.Pending.SelectMany(value => value.ToPayload()).SequenceEqual(Convert.FromHexString(row.GetProperty("pending_hex").GetString()!)), "all pending mouseover bytes");
            Check(mouseovers.Active.Active.SelectMany(value => value.ToPayload()).SequenceEqual(Convert.FromHexString(row.GetProperty("active_hex").GetString()!)), "all active mouseover bytes");
        }
        return checks;
        void Check(bool value, string why) { if (!value) throw new Exception("Mouseover scratch: " + why); checks++; }
    }
}
