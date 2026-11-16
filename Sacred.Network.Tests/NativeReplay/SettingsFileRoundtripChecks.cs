using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Networking;

internal static class SettingsFileRoundtripChecks
{
    public static int Run(string root)
    {
        using var saveEvidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-save.json")));
        var save = SacredSettingsSaveDefinitions.Read(ListenPortChecks.Image(saveEvidence.RootElement));
        var load = SacredSettingsFileDefinitions.Read(ListenPortChecks.Image(saveEvidence.RootElement));
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-file-roundtrip.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var document = new SacredSettingsDocument(); document.SetPath("old.cfg"u8.ToArray());
            foreach (var entry in row.GetProperty("entries").EnumerateArray()) document.Values.Insert(Convert.FromHexString(entry[0].GetString()!), Convert.FromHexString(entry[1].GetString()!));
            var delimiter = Convert.FromHexString(row.GetProperty("delimiter_hex").GetString()!);
            var events = new List<object[]>(); byte[] disk = [];
            var port = new WorldSettingsStreamPort((path, mode) =>
            {
                events.Add(["open", SettingsStorageChecks.Hex(mode.Span)]);
                return new FileMemoryStream(mode.Span.SequenceEqual("wt"u8) ? [] : disk, events, data => disk = data);
            }, error => throw new Exception("Unexpected test file error", error), 0x81, row.GetProperty("buffer_size").GetInt32());
            WorldSettingsFileSaver.Save(save, document, () => delimiter, port.Writer);
            Check(SettingsStorageChecks.Hex(disk) == row.GetProperty("saved_hex").GetString(), "original physical saved bytes");
            Check(Actual().SequenceEqual(Expected("save_events")), "buffered physical write/close sequence");
            events.Clear(); WorldSettingsFileLoader.Load(load, document, "file.cfg"u8.ToArray(), delimiter, port.Reader);
            Check(SettingsStorageChecks.Snapshot(document.Values).SequenceEqual(row.GetProperty("after").EnumerateArray().Select(entry => entry[0].GetString() + ":" + entry[1].GetString())), "original lossy reload contents");
            Check(Actual().SequenceEqual(Expected("load_events")), "raw read/refill/text translation/close sequence");
            // fclose entry is an internal wrapper marker; compare external calls.
            IEnumerable<string> Actual() => events.Where(value => (string)value[0] != "fclose").Select(value => JsonSerializer.Serialize(value));
            IEnumerable<string> Expected(string name) => row.GetProperty(name).EnumerateArray().Where(value => value[0].GetString() != "fclose").Select(value => value.GetRawText());
            void Check(bool pass, string why)
            {
                if (!pass) throw new Exception($"Settings roundtrip buffer{row.GetProperty("buffer_size")}/{row.GetProperty("delimiter_hex")}: {why}"); checks++;
            }
        }
        return checks;
    }
    private sealed class FileMemoryStream : MemoryStream
    {
        private readonly List<object[]> _events;
        private readonly Action<byte[]> _save;
        public FileMemoryStream(byte[] data, List<object[]> events, Action<byte[]> save)
        {
            base.Write(data); Position = 0; _events = events; _save = save;
        }
        public override void Write(ReadOnlySpan<byte> buffer) { _events.Add(["write", SettingsStorageChecks.Hex(buffer)]); base.Write(buffer); }
        public override int Read(Span<byte> buffer)
        {
            var result = base.Read(buffer); _events.Add(["read", buffer.Length, SettingsStorageChecks.Hex(buffer[..result])]); return result;
        }
        public override long Seek(long offset, SeekOrigin origin) { _events.Add(["seek", offset, (int)origin]); return base.Seek(offset, origin); }
        protected override void Dispose(bool disposing)
        {
            _events.Add(["fclose"]); _save(ToArray()); _events.Add(["close-handle"]); base.Dispose(disposing);
        }
    }
}
