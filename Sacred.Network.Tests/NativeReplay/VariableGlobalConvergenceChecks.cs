using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class VariableGlobalConvergenceChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-variable-global-convergence.json")));
        var checks = 0;
        foreach (var scenario in fixture.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            var initial = scenario.GetProperty("initial").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true)
                { VariableGlobals = new(initial) };
            new WorldScriptExecutor(context).ExecuteStartup(Convert.FromHexString(scenario.GetProperty("code_hex").GetString()!));
            var analysis = context.VariableGlobals.Analyze();
            var variants = scenario.GetProperty("cases").EnumerateArray().Select(row =>
                row.GetProperty("globals").EnumerateArray().Select(value => value.GetInt32()).ToArray()).ToArray();
            Check(variants.Length == 150, "all possible supplied destinations executed natively");
            var unknown = 0;
            for (var word = 0; word < 150; word++)
            {
                var results = variants.Select(values => values[word]).Distinct().ToArray();
                if (results.Length == 1) Check(analysis.Values[word] == results[0], "native final-value convergence");
                else { Check(analysis.Values[word] is null, "native alternatives remain ambiguous"); unknown++; }
            }
            Check(analysis.UnknownValueCount == unknown, "ambiguity count");
            Check(context.VariableGlobals.UnresolvedIndexCount > 0, "destination metadata stays unresolved");
            Reject(() => context.VariableGlobals.Apply(), "strict replay does not invent indices");
            if (unknown == 0)
            {
                Check(analysis.ToValues().SequenceEqual(variants[0]), "complete established values");
                foreach (var native in scenario.GetProperty("cases").EnumerateArray())
                    Check(analysis.ToSaveBlock().AsSpan().SequenceEqual(Convert.FromHexString(native.GetProperty("save_hex").GetString()!)), "complete original 600-byte save writer");
            }
            else
            {
                Reject(() => analysis.ToValues(), "ambiguous values cannot materialize");
                Reject(() => analysis.ToSaveBlock(), "ambiguous values cannot serialize");
            }
        }
        Reject(() => new WorldScriptVariableGlobals().Analyze(), "missing initialization");
        Reject(() => new WorldScriptVariableGlobals().Analyze([]), "invalid initialization extent");
        return checks;
        void Check(bool value, string why) { if (!value) throw new Exception("Global convergence: " + why); checks++; }
        void Reject(Action action, string why)
        {
            try { action(); } catch (InvalidDataException) { checks++; return; }
            catch (ArgumentException) { checks++; return; }
            throw new Exception("Global convergence accepted " + why);
        }
    }
}
