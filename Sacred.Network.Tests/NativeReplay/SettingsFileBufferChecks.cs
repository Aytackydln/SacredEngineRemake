using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.Runtime;

internal static class SettingsFileBufferChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-file-buffer.json")));
        var image = ListenPortChecks.Image(native.RootElement);
        var definitions = native.RootElement.GetProperty("variants").EnumerateArray().ToDictionary(variant => variant.GetProperty("name").GetString()!, variant =>
        {
            var changed = image.ToArray();
            foreach (var patch in variant.GetProperty("patches").EnumerateArray())
                Convert.FromHexString(patch.GetProperty("hex").GetString()!).CopyTo(changed, patch.GetProperty("offset").GetInt32());
            return SacredCrtBufferDefinitions.Read(changed);
        });
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var events = new List<object[]>(); var mode = SacredCrtOpenMode.Parse(Convert.FromHexString(row.GetProperty("mode_hex").GetString()!), 0)!.Value;
            var file = new SacredCrtFileSlot { Flags = mode.FileFlags };
            SacredCrtFileBuffer.Allocate(file, definitions[row.GetProperty("variant").GetString()!], size => { events.Add(["allocate", size]); return !row.GetProperty("failed").GetBoolean(); });
            Check(State(file) == row.GetProperty("after").GetRawText(), "allocated FILE state");
            SacredCrtFileBuffer.Free(file, () => events.Add(["free"]));
            Check(State(file) == row.GetProperty("freed").GetRawText(), "freed FILE state");
            Check(Trace(events).SequenceEqual(Trace(row)), "allocation/free calls");
        }
        foreach (var row in native.RootElement.GetProperty("lifetimes").EnumerateArray())
        {
            var source = Convert.FromHexString(row.GetProperty("input_hex").GetString()!); var content = source.ToList(); var cursor = 0; var events = new List<object[]>();
            var blocks = new List<SacredCrtDescriptor[]?> { Enumerable.Range(0, 32).Select(index => new SacredCrtDescriptor { Handle = index < 3 ? (uint)(100 + index) : 0, Flags = index < 3 ? (byte)1 : (byte)0 }).ToArray() };
            var port = new WorldSettingsNativeFilePort(new(new List<SacredCrtFileSlot?> { new() }), new(blocks), definitions["gold"],
                new(new(size => { events.Add(["allocate", size]); return !row.GetProperty("failed").GetBoolean(); }, request =>
                {
                    events.Add(["create", SettingsStorageChecks.Hex(request.Path.Span), request.DesiredAccess, request.ShareMode,
                        new uint[] { 12, 0, request.InheritHandle ? 1u : 0 }, request.CreationDisposition, request.FlagsAndAttributes, 0]);
                    if (request.CreationDisposition == 2) content.Clear(); return 123;
                }, handle => { events.Add(["type", handle]); return 1; }, handle =>
                {
                    events.Add(["close", handle]); return row.GetProperty("close_error").GetUInt32() == 0;
                }, () => row.GetProperty("close_error").GetUInt32(), _ => throw new Exception("Unexpected update path")),
                (handle, buffer) =>
                {
                    var bytes = content.Skip(cursor).Take(buffer.Length).ToArray(); bytes.CopyTo(buffer); cursor += bytes.Length;
                    events.Add(["read", buffer.Length, SettingsStorageChecks.Hex(bytes)]); return new(true, bytes.Length);
                }, (handle, bytes) =>
                {
                    events.Add(["write", SettingsStorageChecks.Hex(bytes.Span)]); content.AddRange(bytes.ToArray()); cursor += bytes.Length; return new(true, bytes.Length);
                }, (handle, offset, origin) =>
                {
                    events.Add(["seek", offset, origin]); cursor = (origin == 0 ? 0 : origin == 1 ? cursor : content.Count) + offset; return unchecked((uint)cursor);
                }, () => events.Add(["free"])), 0, 0x4000, 0x12);
            var mode = Convert.FromHexString(row.GetProperty("mode_hex").GetString()!); var reader = port.Reader; var writer = port.Writer;
            var handle = reader.Open("file.cfg"u8.ToArray(), mode); var file = port.CurrentFile!;
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                if (mode[0] == 'r')
                {
                    var value = reader.ReadLine(handle, 7);
                    Check(value.HasValue == step.GetProperty("returned").GetBoolean() && SettingsStorageChecks.Hex(value?.ToArray() ?? []) == step.GetProperty("bytes_hex").GetString(), "lazy fgets result");
                }
                else
                {
                    var nul = Array.IndexOf(source, (byte)0); var value = source.AsMemory(0, nul < 0 ? source.Length : nul);
                    Check(unchecked((uint)writer.Write(handle, value)) == step.GetProperty("returned").GetUInt32(), "lazy printf result");
                }
            }
            Check(State(file) == row.GetProperty("before").GetRawText(), "pre-close buffer/flags");
            Check(unchecked((uint)writer.Close(handle)) == row.GetProperty("close_return").GetUInt32(), "fclose result");
            Check(State(file) == row.GetProperty("after").GetRawText(), "post-close ownership");
            Check(Trace(events).SequenceEqual(Trace(row)), "whole open/lazy I/O/free/close order");
            Check(SettingsStorageChecks.Hex(content.ToArray()) == row.GetProperty("content_hex").GetString(), "physical contents");
        }
        return checks;
        void Check(bool pass, string why) { if (!pass) throw new Exception("FILE buffer: " + why); checks++; }
    }
    private static string State(SacredCrtFileSlot file) => JsonSerializer.Serialize(new { flags = file.Flags, size = file.BufferSize, storage = file.Storage.ToString().ToLowerInvariant() });
    private static IEnumerable<string> Trace(List<object[]> values) => values.Select(value => JsonSerializer.Serialize(value));
    private static IEnumerable<string> Trace(JsonElement row) => row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText());
}
