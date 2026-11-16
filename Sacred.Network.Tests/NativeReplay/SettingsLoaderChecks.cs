using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.Runtime;

internal static class SettingsLoaderChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-loader.json")));
        var definitions = native.RootElement.GetProperty("variants").EnumerateArray().ToDictionary(variant => variant.GetProperty("name").GetString()!, variant =>
        {
            var image = ListenPortChecks.Image(native.RootElement);
            foreach (var patch in variant.GetProperty("patches").EnumerateArray())
                Convert.FromHexString(patch.GetProperty("hex").GetString()!).CopyTo(image, patch.GetProperty("offset").GetInt32());
            return SacredSettingsFileDefinitions.Read(image);
        });
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var definition = definitions[row.GetProperty("variant").GetString()!];
            var content = Convert.FromHexString(row.GetProperty("input_hex").GetString()!);
            var separators = Convert.FromHexString(row.GetProperty("separators_hex").GetString()!);
            var document = new SacredSettingsDocument(); document.SetPath("old.cfg"u8.ToArray());
            document.Values.Insert("old"u8.ToArray(), "first"u8.ToArray()); document.Values.Insert("old"u8.ToArray(), "second"u8.ToArray());
            var cursor = 0; var events = new List<object[]>();
            WorldSettingsFileLoader.Load(definition, document, "file.cfg\0tail"u8.ToArray(), separators, new((path, mode) =>
            {
                events.Add(["open", SettingsStorageChecks.Hex(path.Span), SettingsStorageChecks.Hex(mode.Span)]);
                return row.GetProperty("fail").GetBoolean() ? 0u : 0x76543210u;
            }, (handle, capacity) =>
            {
                if (cursor == content.Length) { events.Add(["read", handle, capacity, null!]); return null; }
                var end = Math.Min(content.Length, cursor + capacity - 1);
                var newline = Array.IndexOf(content, (byte)'\n', cursor, end - cursor);
                if (newline >= 0) end = newline + 1;
                var chunk = content.AsMemory(cursor, end - cursor); cursor = end;
                events.Add(["read", handle, capacity, SettingsStorageChecks.Hex(chunk.Span)]); return chunk;
            }, handle => events.Add(["close", handle])));
            Check(SettingsStorageChecks.Snapshot(document.Values).SequenceEqual(SettingsStorageChecks.Entries(row)), "duplicate-preserving sorted contents");
            Check(SettingsStorageChecks.Hex(document.Path.Span) == row.GetProperty("path_hex").GetString(), "owned full path");
            Check(events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), "file calls, modes, chunks and capacities");
            void Check(bool pass, string why)
            {
                if (!pass) throw new Exception($"Settings loader {row.GetProperty("variant")}/{row.GetProperty("input_hex")}/{row.GetProperty("separators_hex")}: {why}"); checks++;
            }
        }
        return checks;
    }
}
