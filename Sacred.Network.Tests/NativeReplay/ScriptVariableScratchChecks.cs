using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class ScriptVariableScratchChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-script-variable-scratch.json")));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true);
            new WorldScriptExecutor(context).ExecuteStartup(Convert.FromHexString(row.GetProperty("code_hex").GetString()!));
            var storage = row.GetProperty("name_storage").EnumerateArray()
                .Select(value => Convert.FromHexString(value.GetString()!)).ToArray();
            Check(context.StartupCompleted && context.VariableGlobals.Mutations.Count == storage.Length, "dispatcher sequence completion");
            var actual = context.VariableGlobals.ApplyWithNameStorage(new int[150], (occurrence, _) => storage[occurrence]);
            var expected = row.GetProperty("globals").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            for (var word = 0; word < expected.Length; word++)
                Check(actual[word] == expected[word], $"native global word {word}");
            Check(context.VariableGlobals.UnresolvedIndexCount == (row.TryGetProperty("unresolved", out var unresolved) ? unresolved.GetInt32() : 0), "consecutive callers establish all short indices");
            Reject(() => context.VariableGlobals.Apply(), "missing initialization");
            var unknown = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true);
            new WorldScriptExecutor(unknown).ExecuteStartup(Convert.FromHexString("43000C000178000B07000000"));
            context = unknown;
            Reject(() => context.VariableGlobals.ApplyWithNameStorage(new int[150], (_, _) => null), "missing occurrence storage");
            Reject(() => context.VariableGlobals.ApplyWithNameStorage(new int[150], (_, _) => new byte[31]), "truncated storage");
            Reject(() => context.VariableGlobals.ApplyWithNameStorage(new int[150], (_, _) => new byte[32]), "wrong occurrence name");
            var unterminated = Enumerable.Repeat((byte)'1', 32).ToArray();
            unterminated[0] = (byte)'x'; unterminated[1] = 0;
            Reject(() => context.VariableGlobals.ApplyWithNameStorage(new int[150], (_, _) => unterminated), "suffix needs further stack bytes");
        }
        return checks;
        void Check(bool value, string why) { if (!value) throw new Exception("Variable scratch: " + why); checks++; }
        void Reject(Action action, string why)
        {
            try { action(); } catch (InvalidDataException) { checks++; return; }
            throw new Exception("Variable scratch accepted " + why);
        }
    }
}
