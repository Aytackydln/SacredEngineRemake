using System.Text.Json;
using Sacred.Core.Runtime;

internal static class SettingsOpenChecks
{
    public static int RunModes(string root)
    {
        using var native = Evidence(root, "native-settings-open-mode"); var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var spec = row.GetProperty("spec"); var events = new List<object[]>(); var files = Files(spec);
            var file = files.Acquire(count => { events.Add(["allocate", count]); return !Flag(spec, "allocation_fail"); });
            if (file != null)
            {
                var mode = SacredCrtOpenMode.Parse(Convert.FromHexString(row.GetProperty("mode_hex").GetString()!), Word(spec, "commode", 0));
                if (mode is not { } parsed) file = null;
                else
                {
                    events.Add(["sopen", SettingsStorageChecks.Hex("file.cfg"u8), parsed.OpenFlags, Share(row), 0x1a4]);
                    if (Flag(spec, "sopen_fail")) file = null;
                    else { file.Flags = parsed.FileFlags; file.Descriptor = 3; }
                }
            }
            Check((file != null) == row.GetProperty("returned").GetBoolean() && (file is null || file.Words().SequenceEqual(row.GetProperty("file_words").EnumerateArray().Select(value => value.GetUInt32()))), "FILE decisions/fields");
            Check(Trace(events).SequenceEqual(Trace(row)), "allocation/parser flags/share/permission");
            void Check(bool pass, string why) { if (!pass) throw new Exception($"Open mode {row.GetProperty("mode_hex")}/{spec}: {why}"); checks++; }
        }
        return checks;
    }
    public static int Run(string root)
    {
        using var native = Evidence(root, "native-settings-open"); var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var spec = row.GetProperty("spec"); var events = new List<object[]>(); uint lastError = 0;
            var blocks = new List<SacredCrtDescriptor[]?> { Enumerable.Range(0, 32).Select(index => new SacredCrtDescriptor
            { Handle = index < Word(spec, "occupied", 3) ? (uint)(100 + index) : 0, Flags = index < Word(spec, "occupied", 3) ? (byte)1 : (byte)0 }).ToArray() };
            var descriptors = new SacredCrtDescriptorPool(blocks);
            var file = SacredCrtFileOpen.Open("file.cfg\0tail"u8.ToArray(), Convert.FromHexString(row.GetProperty("mode_hex").GetString()!), Files(spec), descriptors,
                new(count => { events.Add(["allocate", count]); return !Flag(spec, "allocation_fail"); }, request =>
                {
                    events.Add(["create", SettingsStorageChecks.Hex(request.Path.Span), request.DesiredAccess, request.ShareMode,
                        new uint[] { 12, 0, request.InheritHandle ? 1u : 0 }, request.CreationDisposition, request.FlagsAndAttributes, 0]);
                    lastError = Word(spec, "create_error", 0); return lastError == 0 ? 123u : uint.MaxValue;
                }, handle => { events.Add(["type", handle]); lastError = Word(spec, "type_error", 0); return Word(spec, "type", 1); },
                handle => { events.Add(["close", handle]); return true; }, () => lastError,
                _ => throw new Exception("Unexpected read-write text preparation in this matrix")),
                Word(spec, "commode", 0), Word(spec, "fmode", 0x4000), Word(spec, "umask", 0x12), Share(row));
            Check((file != null) == row.GetProperty("returned").GetBoolean() && (file is null || file.Words().SequenceEqual(row.GetProperty("file_words").EnumerateArray().Select(value => value.GetUInt32()))), "FILE decisions/fields");
            Check(Trace(events).SequenceEqual(Trace(row)), "exact CreateFileA/type/close/allocation calls");
            Check(descriptors.OpenDescriptors().Select(item => JsonSerializer.Serialize(new object[] { item.Index, item.Slot.Handle, item.Slot.Flags }))
                .SequenceEqual(row.GetProperty("descriptors").EnumerateArray().Select(value => value.GetRawText())), "all descriptor blocks/flags/handles");
            void Check(bool pass, string why) { if (!pass) throw new Exception($"Open {row.GetProperty("mode_hex")}/{Share(row)}/{spec}: {why}"); checks++; }
        }
        return checks;
    }
    private static JsonDocument Evidence(string root, string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/" + name + ".json")));
    private static IEnumerable<string> Trace(List<object[]> events) => events.Select(value => JsonSerializer.Serialize(value));
    private static IEnumerable<string> Trace(JsonElement row) => row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText());
    private static bool Flag(JsonElement spec, string name) => spec.TryGetProperty(name, out var value) && value.GetBoolean();
    private static uint Word(JsonElement spec, string name, uint otherwise) => spec.TryGetProperty(name, out var value) ? value.GetUInt32() : otherwise;
    private static uint Share(JsonElement row) => row.GetProperty("share").ValueKind == JsonValueKind.Null ? 0x40 : row.GetProperty("share").GetUInt32();
    private static SacredCrtFilePool Files(JsonElement spec) => new(Enumerable.Range(0, (int)Word(spec, "file_slots", 1))
        .Select(_ => Flag(spec, "new_file") ? null : new SacredCrtFileSlot()).ToList());
}
