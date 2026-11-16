using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.Runtime;

internal static class SettingsSaveChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-save.json")));
        var definitions = native.RootElement.GetProperty("variants").EnumerateArray().ToDictionary(variant => variant.GetProperty("name").GetString()!, variant =>
        {
            var image = ListenPortChecks.Image(native.RootElement);
            foreach (var patch in variant.GetProperty("patches").EnumerateArray())
                Convert.FromHexString(patch.GetProperty("hex").GetString()!).CopyTo(image, patch.GetProperty("offset").GetInt32());
            return SacredSettingsSaveDefinitions.Read(image);
        });
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var document = new SacredSettingsDocument(); document.SetPath("file.cfg\0tail"u8.ToArray());
            foreach (var entry in row.GetProperty("entries").EnumerateArray())
                document.Values.Insert(Convert.FromHexString(entry[0].GetString()!), Convert.FromHexString(entry[1].GetString()!));
            var delimiter = Convert.FromHexString(row.GetProperty("delimiter_hex").GetString()!);
            var events = new List<object[]>(); var output = new List<byte>(); var prints = 0; var mutation = row.GetProperty("mutation").GetString();
            WorldSettingsFileSaver.Save(definitions[row.GetProperty("variant").GetString()!], document, () => delimiter, new((path, mode) =>
            {
                events.Add(["open", SettingsStorageChecks.Hex(path.Span), SettingsStorageChecks.Hex(mode.Span)]);
                if (mutation == "open-value") document.Values.Write(document.Values.Snapshot().Last().Key, "OPEN"u8.ToArray());
                return row.GetProperty("fail_open").GetBoolean() ? 0u : 0x6008000u;
            }, (handle, bytes) =>
            {
                var fail = prints++ == row.GetProperty("fail_write").GetInt32();
                events.Add(["write", fail ? null! : SettingsStorageChecks.Hex(bytes.Span)]);
                if (!fail) output.AddRange(bytes.ToArray());
                if (mutation == "write-value" && prints == 1) document.Values.Write(document.Values.Snapshot().Last().Key, "LATER"u8.ToArray());
                if (mutation == "write-separator" && prints == 1) delimiter = "NEW"u8.ToArray();
                return fail ? -1 : bytes.Length;
            }, handle => { events.Add(["close", handle]); return row.GetProperty("fail_close").GetBoolean() ? -1 : 0; }));
            Check(SettingsStorageChecks.Hex(output.ToArray()) == row.GetProperty("output_hex").GetString(), "original fprintf bytes");
            Check(SettingsStorageChecks.Snapshot(document.Values).SequenceEqual(row.GetProperty("after").EnumerateArray().Select(entry => entry[0].GetString() + ":" + entry[1].GetString())), "live ordered values");
            Check(events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), "write/close failure and delimiter timing");
            void Check(bool pass, string why)
            {
                if (!pass) throw new Exception($"Settings save {row.GetProperty("variant")}/{mutation}/{row.GetProperty("fail_open")}/{row.GetProperty("fail_write")}: {why}"); checks++;
            }
        }
        return checks;
    }
}
