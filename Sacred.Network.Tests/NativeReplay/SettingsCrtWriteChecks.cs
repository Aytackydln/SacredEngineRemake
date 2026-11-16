using System.Text.Json;
using Sacred.Core.Runtime;

internal static class SettingsCrtWriteChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-crt-write.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var source = Convert.FromHexString(row.GetProperty("input_hex").GetString()!); var output = new List<byte>();
            var state = new SacredCrtOutputState(row.GetProperty("flags").GetByte()); var events = new List<object[]>(); var calls = 0; var position = 0;
            var result = SacredCrtOutput.Write(state, new(bytes =>
            {
                if (calls++ == row.GetProperty("fail_at").GetInt32())
                {
                    events.Add(["write", SettingsStorageChecks.Hex(bytes.Span), null!]); return new(false, 0, row.GetProperty("error").GetUInt32());
                }
                var count = Math.Min(bytes.Length, row.GetProperty("write_limit").GetInt32()); output.AddRange(bytes[..count].ToArray()); position += count;
                events.Add(["write", SettingsStorageChecks.Hex(bytes.Span), count]); return new(true, count);
            }, (offset, origin) => { events.Add(["seek", offset, origin]); position = 50; return true; }), source);
            Check(unchecked((uint)result) == row.GetProperty("returned").GetUInt32(), "CRT return arithmetic");
            Check(SettingsStorageChecks.Hex(output.ToArray()) == row.GetProperty("output_hex").GetString(), "physical bytes");
            Check(position == row.GetProperty("position").GetInt32() && state.Flags == row.GetProperty("after_flags").GetByte(), "position/flags");
            Check(events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), "physical write and append seek calls");
            void Check(bool pass, string why)
            {
                if (!pass) throw new Exception($"CRT write {row.GetProperty("flags")}/{row.GetProperty("write_limit")}/{row.GetProperty("fail_at")}/{row.GetProperty("input_hex")}: {why}"); checks++;
            }
        }
        return checks;
    }
}
