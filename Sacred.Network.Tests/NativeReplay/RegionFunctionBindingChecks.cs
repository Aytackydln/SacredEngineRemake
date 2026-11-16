using System.Text;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.GameplayHost.Scripting;

internal static class RegionFunctionBindingChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-region-function-bindings.json")));
        var data = native.RootElement; var image = ListenPortChecks.Image(data);
        var definitions = data.GetProperty("variants").EnumerateArray().ToDictionary(row => row.GetProperty("name").GetString()!, row =>
        {
            var copy = image.ToArray();
            foreach (var patch in row.GetProperty("patches").EnumerateArray())
                Convert.FromHexString(patch.GetProperty("hex").GetString()!).CopyTo(copy, patch.GetProperty("offset").GetInt32());
            return SacredRegionFunctionBindingDefinitions.Read(copy);
        });
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var names = row.GetProperty("names").EnumerateArray().Select(value => Encoding.Latin1.GetString(Convert.FromHexString(value.GetString()!)).Split('\0')[0]).ToArray();
            var bindings = new WorldScriptRegionFunctionBindings(names, definitions[row.GetProperty("variant").GetString()!]);
            var expected = row.GetProperty("rows");
            if (bindings.Records.Count != expected.GetArrayLength()) throw new Exception("Region binding count differs from native.");
            checks++;
            foreach (var record in expected.EnumerateArray())
            {
                var name = Encoding.Latin1.GetString(Convert.FromHexString(record[0].GetString()!));
                if (bindings.Find(name) != record[1].GetUInt32()) throw new Exception($"Region binding differs from native: {name}");
                checks++;
            }
        }
        return checks;
    }
}
