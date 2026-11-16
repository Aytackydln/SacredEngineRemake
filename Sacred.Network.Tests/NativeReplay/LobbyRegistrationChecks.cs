using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.GameplayHost.Networking;

internal static class LobbyRegistrationChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-lobby-registration.json")));
        var data = native.RootElement;
        var image = new byte[data.GetProperty("image_size").GetInt32()];
        foreach (var chunk in data.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var definitions = data.GetProperty("variants").EnumerateArray().ToDictionary(value => value.GetProperty("name").GetString()!, value =>
        {
            var variant = image.ToArray();
            foreach (var patch in value.GetProperty("patches").EnumerateArray())
                Convert.FromHexString(patch.GetProperty("hex").GetString()!).CopyTo(variant, patch.GetProperty("offset").GetInt32());
            return SacredLobbyRegistrationDefinitions.Read(variant);
        });
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var definition = definitions[row.GetProperty("variant").GetString()!];
            var events = new List<object[]>();
            WorldLobbyRegistration.Register(definition, new(capacity =>
            {
                events.Add(["directory", capacity]);
                return Convert.FromHexString(row.GetProperty("directory_hex").GetString()!);
            }, (rootKey, key, access) =>
            {
                events.Add(["create", rootKey, Hex(key), access]);
                return (row.GetProperty("status").GetInt32(), 123);
            }, (handle, key, type, bytes) => events.Add(["set", handle, Hex(key), type, Hex(bytes)]),
                handle => events.Add(["close", handle])));
            if (!events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())))
                throw new Exception($"Lobby registration {row.GetProperty("directory_hex")} status={row.GetProperty("status")}: {JsonSerializer.Serialize(events)} expected {row.GetProperty("events").GetRawText()}");
            checks++;
        }
        return checks;
    }

    private static string Hex(ReadOnlyMemory<byte> value) => Convert.ToHexString(value.Span).ToLowerInvariant();
}
