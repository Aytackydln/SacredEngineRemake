using System.Text.Json;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;

internal static class SettingsCrtReadChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-crt-read.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var content = Convert.FromHexString(row.GetProperty("input_hex").GetString()!); var cursor = 0; var reads = 0;
            var count = row.GetProperty("count").GetInt32(); var events = new List<object[]>();
            var state = new SacredCrtInputState(row.GetProperty("flags").GetByte());
            var services = new SacredCrtReadServices(buffer =>
            {
                if (reads++ == row.GetProperty("fail_at").GetInt32())
                {
                    events.Add(["read", buffer.Length, null!]); return new(false, 0, row.GetProperty("error").GetUInt32());
                }
                var length = Math.Min(content.Length - cursor, Math.Min(buffer.Length, row.GetProperty("read_limit").GetInt32()));
                content.AsMemory(cursor, length).CopyTo(buffer); cursor += length;
                events.Add(["read", buffer.Length, SettingsStorageChecks.Hex(buffer.Span[..length])]); return new(true, length);
            }, (offset, origin) => { cursor += offset; events.Add(["seek", offset, origin]); return true; });
            var file = new SacredCrtBufferedInput(state, services, row.GetProperty("buffer_size").GetInt32());
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                events.Clear(); byte[] bytes; var returned = 0;
                if (row.GetProperty("kind").GetString() == "read")
                {
                    var buffer = new byte[count]; returned = SacredCrtInput.Read(state, services, buffer);
                    bytes = buffer.AsSpan(0, Math.Max(0, returned)).ToArray();
                    Check(unchecked((uint)returned) == step.GetProperty("returned").GetUInt32(), "read result");
                }
                else
                {
                    var line = file.ReadLine(count); bytes = line?.ToArray() ?? [];
                    Check(line.HasValue == step.GetProperty("returned").GetBoolean(), "fgets result/EOF");
                }
                Check(SettingsStorageChecks.Hex(bytes) == step.GetProperty("bytes_hex").GetString(), "delivered bytes including NUL");
                Check(state.Flags == step.GetProperty("flags").GetByte() && state.Lookahead == step.GetProperty("lookahead").GetByte() && cursor == step.GetProperty("position").GetInt32() && file.FileFlags == step.GetProperty("file_flags").GetUInt32(), "descriptor/buffer state and physical position");
                Check(events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(step.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), "physical reads, capacities and seek timing");
                void Check(bool pass, string why)
                {
                    if (!pass) throw new Exception($"CRT {row.GetProperty("kind")}/{row.GetProperty("input_hex")}/{row.GetProperty("flags")}/{count}/{row.GetProperty("read_limit")}/{row.GetProperty("fail_at")}: {why}"); checks++;
                }
            }
        }
        return checks;
    }
}
