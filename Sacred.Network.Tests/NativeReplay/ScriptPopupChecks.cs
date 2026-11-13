using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

internal static class ScriptPopupChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-script-popup.json")));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true)
                { HasActiveHero = row.GetProperty("present").GetBoolean() };
            context.Registrations.Import(Bytes(row, "table_hex").Chunk(80).Select(bytes => SacredScriptDialog.FromPayload(bytes)));
            if (row.GetProperty("complete").GetBoolean())
            {
                new WorldScriptExecutor(context).ExecuteStartup(Bytes(row, "command_hex"));
                Check(context.StartupCompleted && context.ExecutedCommands == 1 && context.LocalGuiRequests.Count == 0, "native absent/missing/empty path completes before UI effects");
            }
            else
            {
                Check(row.GetProperty("selected").GetInt32() == 1 && row.GetProperty("hero_lookups").GetInt32() == 1, "native first C-locale registration match reaches present-hero boundary");
                try { new WorldScriptExecutor(context).ExecuteStartup(Bytes(row, "command_hex")); throw new Exception("Unverified present-hero POPUP accepted"); }
                catch (WorldScriptExecutionFailure) { Check(context.IsInvalidated && context.ExecutedCommands == 0, "present-hero lifecycle remains guarded"); }
            }
            Check(Enumerable.Range(0, context.Registrations.Count).SelectMany(context.Registrations.GetRecord).SequenceEqual(Bytes(row, "after_table_hex")), "registration bytes remain intact");
        }
        return checks;
        void Check(bool pass, string why) { if (!pass) throw new Exception("POPUP: " + why); checks++; }
    }
    private static byte[] Bytes(JsonElement row, string name) => Convert.FromHexString(row.GetProperty(name).GetString()!);
}
