using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Sacred.GameplayHost.Networking;

internal static class SessionAnsiConversionChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-session-ansi-conversion.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var direction = row.GetProperty("direction").GetString()!; var mode = row.GetProperty("mode").GetString();
            var allocation = row.GetProperty("allocation").GetByte(); var source = Convert.FromHexString(row.GetProperty("input_hex").GetString()!);
            var events = new List<object[]>(); var queryCount = 0;
            var services = new WorldAnsiConversionServices((value, buffer) =>
            {
                var end = value.Span.IndexOf((byte)0); var bytes = (end < 0 ? value.Span : value.Span[..end]).ToArray();
                events.Add(["decode", Hex(bytes), buffer.Length]);
                var produced = bytes.Select(value => (char)(value + (mode == "wide" ? 0x100 : 0))).Append('\0').ToArray();
                if (buffer.IsEmpty)
                {
                    queryCount = mode == "query-fail" ? 0 : produced.Length; Mutate("query"); return queryCount;
                }
                if (mode == "fill-fail") return 0;
                if (mode == "fill-short") produced = ['Z', '\0'];
                produced.AsSpan(0, Math.Min(produced.Length, buffer.Length)).CopyTo(buffer.Span); return Math.Min(produced.Length, buffer.Length);
            }, (value, buffer) =>
            {
                events.Add(["encode", Hex(MemoryMarshal.AsBytes(value.Span)), buffer.Length]);
                var produced = value.ToArray().Select(value => unchecked((byte)value)).ToArray();
                if (buffer.IsEmpty)
                {
                    queryCount = mode == "query-fail" ? 0 : produced.Length; Mutate("query"); return queryCount;
                }
                if (mode == "fill-fail") return 0;
                if (mode == "fill-short") produced = [(byte)'Z', 0];
                produced.AsSpan(0, Math.Min(produced.Length, buffer.Length)).CopyTo(buffer.Span); return Math.Min(produced.Length, buffer.Length);
            }, count =>
            {
                Mutate("allocate"); return Enumerable.Repeat((char)(allocation * 0x101), count).ToArray();
            }, count =>
            {
                Mutate("allocate"); return Enumerable.Repeat(allocation, count).ToArray();
            });
            byte[] returned = Encoding.Unicode.GetBytes("old"); string? error = null; var countOutput = 0;
            try
            {
                if (direction == "decode")
                {
                    var value = WorldAnsiStringConversion.Decode(services, () => source);
                    if (value.HasValue) returned = MemoryMarshal.AsBytes(value.Value.Span).ToArray();
                }
                else
                {
                    var value = WorldAnsiStringConversion.Encode(services, () => MemoryMarshal.Cast<byte, char>(source.AsSpan()).ToArray());
                    returned = value.Value.ToArray(); countOutput = value.RequiredByteCount;
                }
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("scanned beyond")) { error = "decode-buffer-overread"; }
            Check(error == row.GetProperty("error").GetString(), "bounded native overread");
            Check(error != null || Hex(returned) == row.GetProperty("returned_hex").GetString(), "destination/owned result bytes");
            Check(queryCount == row.GetProperty("query_count").GetInt32() && (direction != "encode" || countOutput == row.GetProperty("count_output").GetInt32()), "first query count and native optional output");
            Check(events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), "API order, source rereads and capacities");
            void Mutate(string stage)
            {
                if (mode == stage + "-source") source = direction == "decode" ? "NEW\0tail"u8.ToArray() : Encoding.Unicode.GetBytes("NEW\0tail");
            }
            void Check(bool pass, string why)
            {
                if (!pass) throw new Exception($"ANSI conversion {direction}/{mode}/{allocation}/{row.GetProperty("input_hex")}: {why}"); checks++;
            }
        }
        return checks;
    }
    private static string Hex(ReadOnlySpan<byte> value) => Convert.ToHexString(value).ToLowerInvariant();
}
