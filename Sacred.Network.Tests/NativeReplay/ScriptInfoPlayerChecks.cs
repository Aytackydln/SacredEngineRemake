using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class ScriptInfoPlayerChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-script-info-player.json")));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var multiplayer = row.GetProperty("multiplayer").GetBoolean();
            var code = Convert.FromHexString(row.GetProperty("command_hex").GetString()!);
            var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, multiplayer);
            if (multiplayer || row.GetProperty("values").GetArrayLength() == 0)
            {
                new WorldScriptExecutor(context).ExecuteStartup(code);
                Check(context.StartupCompleted && context.ExecutedCommands == 1 &&
                    context.LocalGuiRequests.Count == row.GetProperty("steps").GetArrayLength(), "actual multiplayer exclusion and empty numeric input");
            }
            else
            {
                var words = row.GetProperty("steps")[0].GetProperty("words").EnumerateArray().Select(value => value.GetUInt32());
                Check(words.SequenceEqual(new uint[] { 0x13, 4, 1, 0, 0 }), "native offline GUI positive control");
                try { new WorldScriptExecutor(context).ExecuteStartup(code); throw new Exception("Unverified offline InfoPlayer accepted"); }
                catch (WorldScriptExecutionFailure) { Check(context.IsInvalidated && context.ExecutedCommands == 0 && context.LocalGuiRequests.Count == 0, "offline UI lifecycle stays guarded"); }
            }
        }
        return checks;
        void Check(bool pass, string why) { if (!pass) throw new Exception("InfoPlayer: " + why); checks++; }
    }
}
