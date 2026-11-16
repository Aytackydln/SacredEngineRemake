using System.Text.Json;
using Sacred.Core.Runtime;

internal static class SettingsUpdateChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-update.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var spec = row.GetProperty("spec"); var events = new List<object[]>(); uint error = 0;
            var content = Convert.FromHexString(spec.GetProperty("content_hex").GetString()!).ToList();
            var position = 0; var seekCalls = 0; var readCalls = 0;
            var block = Enumerable.Range(0, 32).Select(index => new SacredCrtDescriptor
                { Handle = index < 3 ? (uint)(100 + index) : 0, Flags = index < 3 ? (byte)1 : (byte)0, Lookahead = 10 }).ToArray();
            var descriptors = new SacredCrtDescriptorPool(new List<SacredCrtDescriptor[]?> { block });
            var file = SacredCrtFileOpen.Open("file.cfg"u8.ToArray(), Convert.FromHexString(row.GetProperty("mode_hex").GetString()!),
                new(new List<SacredCrtFileSlot?> { new() }), descriptors,
                new(count => { events.Add(["allocate", count]); return true; }, request =>
                {
                    events.Add(["create", SettingsStorageChecks.Hex(request.Path.Span), request.DesiredAccess, request.ShareMode,
                        new uint[] { 12, 0, request.InheritHandle ? 1u : 0 }, request.CreationDisposition, request.FlagsAndAttributes, 0]);
                    if (request.CreationDisposition == 2) content.Clear(); return 123;
                }, handle => { events.Add(["type", handle]); return Word("type", 1); },
                handle => { events.Add(["close", handle]); return Word("close_error", 0) == 0; }, () => error,
                descriptor => SacredCrtUpdateOpen.Prepare(descriptor, new(Seek, Read, Truncate, () => error))),
                0, Word("fmode", 0x4000), 0x12);
            Check((file != null) == row.GetProperty("returned").GetBoolean() && (file is null || file.Words().SequenceEqual(row.GetProperty("file_words").EnumerateArray().Select(value => value.GetUInt32()))), "FILE decisions/fields");
            Check(events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), "platform call ordering");
            Check(descriptors.OpenDescriptors().Select(item => JsonSerializer.Serialize(new object[] { item.Index, item.Slot.Handle, item.Slot.Flags }))
                .SequenceEqual(row.GetProperty("descriptors").EnumerateArray().Select(value => value.GetRawText())), "descriptor state");
            Check(position == row.GetProperty("position").GetInt32() && Convert.ToHexString(content.ToArray()).Equals(row.GetProperty("content_hex").GetString(), StringComparison.OrdinalIgnoreCase), "position/physical bytes");
            uint Word(string name, uint otherwise) => spec.TryGetProperty(name, out var value) ? value.GetUInt32() : otherwise;
            void Check(bool pass, string why) { if (!pass) throw new Exception($"Update {row.GetProperty("mode_hex")}/{spec}: {why}; actual {JsonSerializer.Serialize(events)}"); checks++; }
            uint Seek(int offset, int origin)
            {
                events.Add(["seek", offset, origin]); seekCalls++;
                var failed = Word("seek_fail_at", (uint)seekCalls) == seekCalls ? Word("seek_error", 0) : 0;
                if (failed != 0) { error = failed; return uint.MaxValue; }
                var target = (origin == 0 ? 0 : origin == 1 ? position : content.Count) + offset;
                if (target < 0) { error = 131; return uint.MaxValue; }
                position = target; return (uint)position;
            }
            SacredCrtReadResult Read(Memory<byte> destination)
            {
                readCalls++;
                if (Word("read_error", 0) != 0 && readCalls == Word("read_fail_at", 1))
                { error = Word("read_error", 0); events.Add(["read", destination.Length, null!]); return new(false, 0, error); }
                var bytes = content.Skip(position).Take(destination.Length).ToArray(); position += bytes.Length;
                bytes.CopyTo(destination); events.Add(["read", destination.Length, SettingsStorageChecks.Hex(bytes)]); return new(true, bytes.Length, error);
            }
            bool Truncate()
            {
                events.Add(["truncate", position]); error = Word("truncate_error", 0);
                if (error != 0) return false;
                if (position < content.Count) content.RemoveRange(position, content.Count - position); return true;
            }
        }
        return checks;
    }
}
