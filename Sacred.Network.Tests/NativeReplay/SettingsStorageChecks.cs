using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;

internal static class SettingsStorageChecks
{
    public static int Run(string root)
    {
        using var loader = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-loader.json")));
        var definition = SacredSettingsFileDefinitions.Read(ListenPortChecks.Image(loader.RootElement));
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-storage.json")));
        var checks = 0;
        foreach (var lifetime in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var values = new SacredSettingsValues();
            foreach (var row in lifetime.GetProperty("steps").EnumerateArray())
            {
                var key = Convert.FromHexString(row.GetProperty("key_hex").GetString()!);
                var value = Convert.FromHexString(row.GetProperty("value_hex").GetString()!);
                switch (row.GetProperty("kind").GetString())
                {
                    case "insert": values.Insert(key, value); break;
                    case "write": values.Write(key, value); break;
                    case "integer": values.Write(key, SacredSettingsIntegerValue.Format(definition.IntegerFormat.Span, row.GetProperty("number").GetUInt32())); break;
                    case "remove": values.Remove(key); break;
                    case "clear": values.Clear(); break;
                    case "read": Check(Hex(values.Read(key)) == row.GetProperty("returned_hex").GetString(), "copied first equal value"); break;
                }
                Check(values.Count == row.GetProperty("entries").GetArrayLength() && Snapshot(values).SequenceEqual(Entries(row)), "all ordered entries/count");
                void Check(bool pass, string why)
                {
                    if (!pass) throw new Exception($"Settings storage {lifetime.GetProperty("seed")}/{row.GetProperty("kind")}: {why}"); checks++;
                }
            }
        }
        return checks;
    }
    public static IEnumerable<string> Snapshot(SacredSettingsValues values) => values.Snapshot().Select(pair => Hex(pair.Key) + ":" + Hex(pair.Value));
    public static IEnumerable<string> Entries(JsonElement row) => row.GetProperty("entries").EnumerateArray().Select(pair => pair[0].GetString() + ":" + pair[1].GetString());
    public static string Hex(ReadOnlySpan<byte> value) => Convert.ToHexString(value).ToLowerInvariant();
}
