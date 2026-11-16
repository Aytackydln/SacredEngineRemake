using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.Runtime;

internal static class SessionIntegerSettingChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-session-integer-settings.json")));
        var images = new Dictionary<string, byte[]>(); var classes = new Dictionary<string, ushort[]>();
        foreach (var variant in native.RootElement.GetProperty("variants").EnumerateArray())
        {
            var image = ListenPortChecks.Image(native.RootElement);
            foreach (var patch in variant.GetProperty("patches").EnumerateArray())
                Convert.FromHexString(patch.GetProperty("hex").GetString()!).CopyTo(image, patch.GetProperty("offset").GetInt32());
            var name = variant.GetProperty("name").GetString()!; images[name] = image;
            var bytes = Convert.FromHexString(variant.GetProperty("classes").GetString()!);
            classes[name] = Enumerable.Range(0, 256).Select(index => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index * 2))).ToArray();
        }
        var definitions = images.ToDictionary(pair => pair.Key, pair => Enum.GetValues<SacredSessionIntegerSetting>()
            .ToDictionary(setting => setting, setting => SacredSessionIntegerDefinitions.Read(pair.Value, setting)));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var variant = row.GetProperty("variant").GetString()!;
            var setting = Enum.Parse<SacredSessionIntegerSetting>(row.GetProperty("setting").GetString()!);
            var input = Convert.FromHexString(row.GetProperty("input_hex").GetString()!); var events = new List<object[]>();
            var returned = WorldIntegerSetting.Read(definitions[variant][setting], key =>
            {
                events.Add(["read", Hex(key.Span)]); return input;
            }, (key, value) => events.Add(["write-default", Hex(key.Span), value]),
                value => SacredAnsiDecimalParser.Parse(value.Span, classes[variant]));
            Check(returned == row.GetProperty("returned").GetUInt32(), "returned integer");
            Check(events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), "settings lookup/default effects");
            void Check(bool pass, string why)
            {
                if (!pass) throw new Exception($"Integer setting {setting}/{variant}/{row.GetProperty("input_hex")}: {why}"); checks++;
            }
        }
        return checks;
    }
    private static string Hex(ReadOnlySpan<byte> value) => Convert.ToHexString(value).ToLowerInvariant();
}
