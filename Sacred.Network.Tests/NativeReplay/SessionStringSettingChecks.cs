using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Executable;
using Sacred.GameplayHost.Networking;

internal static class SessionStringSettingChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-session-string-settings.json")));
        var definitions = new Dictionary<string, Dictionary<SacredSessionStringSetting, SacredStringSettingDefinition>>();
        foreach (var variant in native.RootElement.GetProperty("variants").EnumerateArray())
        {
            var image = ListenPortChecks.Image(native.RootElement);
            foreach (var patch in variant.GetProperty("patches").EnumerateArray())
                Convert.FromHexString(patch.GetProperty("hex").GetString()!).CopyTo(image, patch.GetProperty("offset").GetInt32());
            definitions[variant.GetProperty("name").GetString()!] = Enum.GetValues<SacredSessionStringSetting>()
                .ToDictionary(setting => setting, setting => SacredSessionStringDefinitions.Read(image, setting));
        }
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var variant = row.GetProperty("variant").GetString()!;
            var setting = Enum.Parse<SacredSessionStringSetting>(row.GetProperty("setting").GetString()!);
            var input = Convert.FromHexString(row.GetProperty("input_hex").GetString()!); var events = new List<object[]>();
            var returned = WorldStringSetting.Read(definitions[variant][setting], key =>
            {
                events.Add(["read", Hex(key.Span)]); return input;
            }, (key, value) => events.Add(["write-default", Hex(key.Span), Hex(value.Span)]));
            Check(Hex(returned.Span) == row.GetProperty("returned_hex").GetString(), "returned owned bytes");
            Check(events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), "lookup/default order and values");
            void Check(bool pass, string why)
            {
                if (!pass) throw new Exception($"String setting {setting}/{variant}/{row.GetProperty("input_hex")}: {why}"); checks++;
            }
        }
        return checks;
    }
    private static string Hex(ReadOnlySpan<byte> value) => Convert.ToHexString(value).ToLowerInvariant();
}
