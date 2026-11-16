using System.Text.Json;
using Sacred.Core.Runtime;

internal static class SettingsBufferedWriteChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-buffered-write.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var events = new List<object[]>(); var output = new List<byte>(); var calls = 0; var position = 0;
            var state = new SacredCrtOutputState(row.GetProperty("flags").GetByte());
            var file = new SacredCrtBufferedOutput(state, new(bytes =>
            {
                if (calls++ == row.GetProperty("fail_at").GetInt32())
                {
                    events.Add(["write", SettingsStorageChecks.Hex(bytes.Span), null!]); return new(false, 0, 5);
                }
                var count = Math.Min(bytes.Length, row.GetProperty("write_limit").GetInt32()); position += count;
                output.AddRange(bytes[..count].ToArray()); events.Add(["write", SettingsStorageChecks.Hex(bytes.Span), count]); return new(true, count);
            }, (offset, origin) => { position = 50; events.Add(["seek", offset, origin]); return true; }), row.GetProperty("buffer_size").GetInt32());
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                events.Clear(); var input = step.GetProperty("input_hex").GetString();
                var result = input is null ? file.Flush() : file.Write(Convert.FromHexString(input));
                Check(unchecked((uint)result) == step.GetProperty("returned").GetUInt32(), "printf/flush result");
                Check(file.FileFlags == step.GetProperty("file_flags").GetUInt32() && state.Flags == step.GetProperty("descriptor_flags").GetByte() && position == step.GetProperty("position").GetInt32(), "FILE/descriptor/position");
                Check(SettingsStorageChecks.Hex(output.ToArray()) == step.GetProperty("output_hex").GetString(), "retained/flushed physical bytes");
                Check(events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(step.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), "write and seek sequence");
                void Check(bool pass, string why)
                {
                    if (!pass) throw new Exception($"Buffered write size{row.GetProperty("buffer_size")}/{row.GetProperty("flags")}/{row.GetProperty("write_limit")}/{row.GetProperty("fail_at")}/{input}: {why}"); checks++;
                }
            }
        }
        return checks;
    }
}
