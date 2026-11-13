using System.Text.Json;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class ScriptRandomVariableChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-script-random-variable.json")));
        var globals = fixture.RootElement.GetProperty("globals_before").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var draws = 0;
            var expectedDraws = row.GetProperty("draws").GetInt32();
            Func<int>? next = expectedDraws == 0 ? null : () => { draws++; return random.Next(); };
            var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true, next);
            context.Variables.Import(row.GetProperty("initial_variables").EnumerateArray()
                .Select(value => GoldScriptVariableRecord.FromPayload(Convert.FromHexString(value.GetString()!))));
            new WorldScriptExecutor(context).ExecuteStartup(Convert.FromHexString(row.GetProperty("command_hex").GetString()!));
            var expected = row.GetProperty("variables").EnumerateArray().SelectMany(value => Convert.FromHexString(value.GetString()!)).ToArray();
            Check(context.Variables.Records.SelectMany(value => value.ToPayload()).SequenceEqual(expected), "complete original variable table, storage and order");
            Check(context.VariableGlobals.Apply(globals).SequenceEqual(row.GetProperty("globals_after").EnumerateArray().Select(value => value.GetInt32())),
                "complete original additional globals, including known short-name suffix");
            Check(draws == expectedDraws && random.State == row.GetProperty("rng_after").GetUInt32(), "exact original shared RNG consumption");
            Check(context.StartupCompleted && !context.IsInvalidated && context.Coverage.GetValueOrDefault((ushort)105) == 1,
                "command credit after the compared effects");
        }
        var missing = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true);
        var example = fixture.RootElement.GetProperty("cases").EnumerateArray().First(row => row.GetProperty("draws").GetInt32() == 1);
        try { new WorldScriptExecutor(missing).ExecuteStartup(Convert.FromHexString(example.GetProperty("command_hex").GetString()!)); throw new Exception("Missing RNG accepted"); }
        catch (WorldScriptExecutionFailure)
        {
            Check(missing.IsInvalidated && missing.Variables.Records.Count == 0 && missing.VariableGlobals.Mutations.Count == 0 && missing.ExecutedCommands == 0,
                "missing RNG invalidates before variable/global mutation or credit");
        }
        return checks;
        void Check(bool value, string message) { if (!value) throw new Exception("Random variable: " + message); checks++; }
    }
}
