using System.Text.Json;
using Sacred.Assets.Executable;

internal static class WeaponHandRefreshChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-weapon-hand-refresh.json")));
        var checks = 0;
        foreach (var variant in fixture.RootElement.GetProperty("variants").EnumerateArray())
        {
            var code = Convert.FromHexString(variant.GetProperty("definition_hex").GetString()!);
            var definitions = SacredWeaponHandRefreshDefinitions.Read(code);
            foreach (var row in variant.GetProperty("cases").EnumerateArray())
            {
                var usage = row.GetProperty("usage");
                var actual = definitions.RequiresRefresh(row.GetProperty("type").GetUInt32(),
                    usage.ValueKind == JsonValueKind.Null ? null : usage.GetByte(), row.GetProperty("parent").GetUInt32());
                if (actual != row.GetProperty("result").GetBoolean())
                    throw new Exception($"Original Weapon hand-refresh predicate differs: {row}");
                checks++;
            }
            var invalid = code.ToArray(); invalid[15 + 3] ^= 1;
            Reject(invalid);
            Reject(code.Concat(code).ToArray());
            Reject([]);
        }
        return checks;
        void Reject(byte[] image)
        {
            try { _ = SacredWeaponHandRefreshDefinitions.Read(image); }
            catch (InvalidDataException) { checks++; return; }
            throw new Exception("Invalid or ambiguous Weapon hand-refresh instructions accepted.");
        }
    }
}
