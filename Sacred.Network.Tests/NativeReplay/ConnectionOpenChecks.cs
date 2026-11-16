using System.Security.Cryptography;
using System.Text.Json;
using Sacred.GameplayHost.Networking;

internal static class ConnectionOpenChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-connection-open.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var fixture = new ConnectionOpenFixture(row);
            var result = WorldConnectionOpen.Open(fixture.Services, row.GetProperty("parameter").GetUInt32(), row.GetProperty("port").GetUInt32(), row.GetProperty("mode").GetByte());
            Check(result == row.GetProperty("returned").GetByte(), "raw return byte");
            Check(fixture.Events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), "service order and operands");
            Check(fixture.Header.SequenceEqual(Convert.FromHexString(row.GetProperty("header").GetString()!)), "connection header");
            Check(Convert.ToHexString(SHA256.HashData(fixture.ResetBytes)).Equals(row.GetProperty("reset_sha256").GetString(), StringComparison.OrdinalIgnoreCase), "complete cleared extent");
            Check((fixture.Memory.ReadWord(0x40450) - 0x6090000) / 52 == row.GetProperty("pending_count").GetUInt32() &&
                fixture.Backend == row.GetProperty("backend").GetUInt32() && fixture.Memory.ReadWord(0x40414) == row.GetProperty("speed").GetUInt32(), "retained state and optional backend");
            void Check(bool pass, string why) { if (!pass) throw new Exception($"Connection open {row.GetProperty("failure")} mode={row.GetProperty("mode")} count={row.GetProperty("count")} {row.GetProperty("mutation")}: {why}"); checks++; }
        }
        return checks;
    }
}
