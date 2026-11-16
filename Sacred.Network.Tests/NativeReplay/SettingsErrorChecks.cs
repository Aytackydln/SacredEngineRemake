using System.Text.Json;
using Sacred.Assets.Executable;

internal static class SettingsErrorChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-errors.json")));
        var data = native.RootElement; var image = ListenPortChecks.Image(data);
        var definitions = data.GetProperty("variants").EnumerateArray().ToDictionary(value => value.GetProperty("name").GetString()!, value =>
        {
            var variant = image.ToArray();
            foreach (var patch in value.GetProperty("patches").EnumerateArray())
                Convert.FromHexString(patch.GetProperty("hex").GetString()!).CopyTo(variant, patch.GetProperty("offset").GetInt32());
            return SacredCrtErrorDefinitions.Read(variant);
        });
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var error = row.GetProperty("error").GetUInt32();
            if (definitions[row.GetProperty("variant").GetString()!].Map(error) != row.GetProperty("errno").GetUInt32() || error != row.GetProperty("doserrno").GetUInt32())
                throw new Exception($"Original CRT error mapping differs: {row}");
            checks += 2;
        }
        return checks;
    }
}
