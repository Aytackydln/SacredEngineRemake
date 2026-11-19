using System.Text.Json;
using Sacred.GameplayHost.Networking;

internal static class HostStartupModeChecks
{
    public static int Run(string root)
    {
        using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-host-startup-mode.json")));
        var checks = 0;
        foreach (var row in proof.RootElement.GetProperty("cases").EnumerateArray())
        {
            var request = new WorldHostStartupRequest(row.GetProperty("selected_mode").GetUInt16(), row.GetProperty("selected_value").GetUInt32());
            Check(request.EngineMode == row.GetProperty("engine_mode").GetUInt32(), "original dispatcher mode");
            Check(request.LoadsStartCode == (row.GetProperty("script_calls").GetArrayLength() != 0), "actual Engine script gate");
            if (row.GetProperty("reset_address").ValueKind != JsonValueKind.Null)
                Check(request == WorldHostStartupRequest.FreshWorld, "original fresh-world reset source");
        }
        return checks;
        void Check(bool condition, string reason) { if (!condition) throw new Exception("Host startup mode: " + reason); checks++; }
    }
}
