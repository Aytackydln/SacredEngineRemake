using System.Runtime.InteropServices;
using System.Numerics;
using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class StartupEffectConsumerChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-startup-effect-consumers.json")));
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var evidence = fixture.RootElement;
        var rows = evidence.GetProperty("cases").EnumerateArray().ToArray();
        Check(rows.Length == 9 && evidence.GetProperty("consumer_calls").GetArrayLength() == 3,
            "Native comparison executes particle update, FX update and complete renderer for all backing variants.");
        Check(evidence.GetProperty("writer_control_reads").EnumerateArray().All(value => value.GetBoolean()) &&
            evidence.GetProperty("writer_control_reads").GetArrayLength() == evidence.GetProperty("spans").GetArrayLength(),
            "Original writer positive control observes every candidate range through the same native read monitor.");
        foreach (var row in rows)
        {
            var zero = rows.Single(peer => peer.GetProperty("elapsed").GetSingle() == row.GetProperty("elapsed").GetSingle() &&
                peer.GetProperty("pattern").GetString() == "zero");
            Check(row.GetProperty("read_count").GetInt32() == 0 && row.GetProperty("steps").GetArrayLength() == 6 &&
                row.GetProperty("draw_count").GetInt32() > 0, "Real update/render consumers do not read the candidate ranges and submit geometry.");
            foreach (var key in new[] { "effect_sha256", "particle_sha256", "submission_sha256", "rng_after", "draw_count" })
                Check(row.GetProperty(key).ToString() == zero.GetProperty(key).ToString(),
                    "Backing variants preserve all other native state, submitted geometry/matrices/textures and RNG: " + key);
        }
        foreach (var row in evidence.GetProperty("projection_cases").EnumerateArray())
        {
            var expected = MemoryMarshal.Read<Vector3>(Convert.FromHexString(row.GetProperty("position_hex").GetString()!));
            var actual = WorldStartupEffectProjection.ComparedGold.Project(row.GetProperty("x").GetInt32(),
                row.GetProperty("y").GetInt32(), row.GetProperty("height").GetSingle());
            Check(actual == expected, "Shared PC=24 projection matches the original projector exactly across startup-scale coordinates.");
        }
        using var receive = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-startup-effect-receive.json")));
        foreach (var row in receive.RootElement.GetProperty("cases").EnumerateArray())
            Check(row.GetProperty("payload_length").GetInt32() == 451 && row.GetProperty("consumed").GetInt32() == 451 &&
                row.GetProperty("origin_hex").GetString() == row.GetProperty("retained_hex").GetString(),
                "Complete original FX reader preserves the transmitted particle origin; it cannot be canonicalized away.");
        return checks;
    }
}
