using System.Text.Json;
using Sacred.GameplayHost.Scripting;
using Sacred.Assets.GameBin;

internal static class ScriptRegistrationScratchChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-script-registration-scratch.json")));
        var checks = 0;
        foreach (var header in fixture.RootElement.GetProperty("initial_headers").EnumerateArray())
            Check(new WorldScriptRegistrationTable().EstablishedCapacity == BitConverter.ToInt32(Convert.FromHexString(header.GetString()!), 8), "original constructor capacity baseline");
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            SacredScriptFunction[] functions = row.TryGetProperty("movement", out _)
                ? [new("reserved", 0, 0, -1, 0, 0), new("one", 0, 0, -1, 1, 0), new("two", 0, 0, -1, 2, 0)] : [];
            var context = new WorldScriptExecutionContext(new(), functions, ReadOnlyMemory<byte>.Empty, true);
            new WorldScriptExecutor(context).ExecuteStartup(Convert.FromHexString(row.GetProperty("code_hex").GetString()!));
            Check(context.StartupCompleted, "native sequence completion");
            Check(context.Registrations.Count == row.GetProperty("count").GetInt32(), "reserved duplicate and append count");
            Check(context.Registrations.EstablishedCapacity == row.GetProperty("capacity").GetInt32(), "actual native capacity after allocations");
            Check(context.VariableGlobals.UnresolvedIndexCount == row.GetProperty("unresolved").GetInt32(), "preservation only on inline append");
            var storage = row.GetProperty("name_storage").EnumerateArray().Select(value => Convert.FromHexString(value.GetString()!)).ToArray();
            var actual = context.VariableGlobals.ApplyWithNameStorage(new int[150], (index, _) => storage[index]);
            var expected = row.GetProperty("globals").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            for (var i = 0; i < expected.Length; i++) Check(actual[i] == expected[i], $"native global word {i}");
        }
        return checks;
        void Check(bool value, string why) { if (!value) throw new Exception("Registration scratch: " + why); checks++; }
    }
}
