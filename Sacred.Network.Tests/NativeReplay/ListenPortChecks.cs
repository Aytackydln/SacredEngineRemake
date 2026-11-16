using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.Runtime;

internal static class ListenPortChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-listen-port.json")));
        var data = native.RootElement; var image = Image(data);
        var definitions = data.GetProperty("variants").EnumerateArray().ToDictionary(value => value.GetProperty("name").GetString()!, value =>
        {
            var variant = image.ToArray();
            foreach (var patch in value.GetProperty("patches").EnumerateArray())
                Convert.FromHexString(patch.GetProperty("hex").GetString()!).CopyTo(variant, patch.GetProperty("offset").GetInt32());
            return SacredListenPortDefinitions.Read(variant);
        });
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var definition = definitions[row.GetProperty("variant").GetString()!];
            var input = Convert.FromHexString(row.GetProperty("input_hex").GetString()!);
            var events = new List<object[]>();
            var services = new WorldListenPortServices(key => { events.Add(["read", Hex(key)]); return input; },
                (key, value) => events.Add(["write-default", Hex(key), value]),
                value => SacredAnsiDecimalParser.Parse(value.Span, definition.DefaultClasses.Span));
            var result = row.GetProperty("kind").GetString() == "getter" ? WorldListenPort.Read(definition, services) :
                unchecked((uint)SacredAnsiDecimalParser.Parse(input, definition.DefaultClasses.Span));
            if (result != row.GetProperty("returned").GetUInt32() || !events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())))
                throw new Exception($"Listen port {row.GetProperty("variant")} {row.GetProperty("kind")} {row.GetProperty("input_hex")}: differs from original.");
            checks += 2;
        }
        foreach (var row in data.GetProperty("key_comparisons").EnumerateArray())
        {
            var actual = SacredSettingsKeyComparer.Instance.Compare(Convert.FromHexString(row.GetProperty("left").GetString()!), Convert.FromHexString(row.GetProperty("right").GetString()!)) < 0;
            if (actual != (row.GetProperty("less").GetByte() != 0)) throw new Exception("Settings key ordering differs from original.");
            checks++;
        }
        foreach (var row in data.GetProperty("ip_cases").EnumerateArray())
        {
            var definition = definitions[row.GetProperty("variant").GetString()!];
            if (SacredAnsiIpv4Parser.Parse(Convert.FromHexString(row.GetProperty("input_hex").GetString()!), definition.DefaultClasses.Span) != row.GetProperty("returned").GetUInt32())
                throw new Exception("Original dotted-address conversion differs.");
            checks++;
        }
        return checks;
    }

    public static byte[] Image(JsonElement data)
    {
        var image = new byte[data.GetProperty("image_size").GetInt32()];
        foreach (var chunk in data.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        return image;
    }
    private static string Hex(ReadOnlyMemory<byte> value) => Convert.ToHexString(value.Span).ToLowerInvariant();
}
