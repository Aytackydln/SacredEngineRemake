using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Networking;

internal static class SessionSettingsImportChecks
{
    private static readonly Dictionary<WorldSessionSetting, string> Names = new()
    {
        [WorldSessionSetting.CdKey] = "cdkey", [WorldSessionSetting.CdKey2] = "cdkey2",
        [WorldSessionSetting.Ip] = "ip", [WorldSessionSetting.Lobby] = "lobby", [WorldSessionSetting.Login] = "login",
        [WorldSessionSetting.LobbyPassword] = "lobbypwd", [WorldSessionSetting.LobbyPort] = "lobbyport",
        [WorldSessionSetting.Password] = "password", [WorldSessionSetting.Player] = "player",
        [WorldSessionSetting.ListenPort] = "port", [WorldSessionSetting.Session] = "session",
        [WorldSessionSetting.Speed] = "speed", [WorldSessionSetting.AseAddress] = "ase-ip", [WorldSessionSetting.AsePort] = "ase-port"
    };

    public static int Run(string root)
    {
        using var port = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-listen-port.json")));
        var numeric = SacredListenPortDefinitions.Read(ListenPortChecks.Image(port.RootElement));
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-session-settings-import.json")));
        var definition = SacredSessionImportDefinitions.Read(ListenPortChecks.Image(native.RootElement));
        using var integerEvidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-session-integer-settings.json")));
        var integerImage = ListenPortChecks.Image(integerEvidence.RootElement);
        var integers = Enum.GetValues<SacredSessionIntegerSetting>().ToDictionary(setting => setting,
            setting => SacredSessionIntegerDefinitions.Read(integerImage, setting));
        using var stringEvidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-session-string-settings.json")));
        var stringImage = ListenPortChecks.Image(stringEvidence.RootElement);
        var stringDefinitions = Enum.GetValues<SacredSessionStringSetting>().ToDictionary(setting => setting,
            setting => SacredSessionStringDefinitions.Read(stringImage, setting));
        using var fileEvidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-loader.json")));
        var fileDefinition = SacredSettingsFileDefinitions.Read(ListenPortChecks.Image(fileEvidence.RootElement));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var spec = row.GetProperty("spec"); var events = new List<object[]>();
            var mutation = spec.TryGetProperty("mutation", out var m) ? m.GetString() : "none";
            var conversion = spec.TryGetProperty("conversion", out var c) ? c.GetString() : "latin1";
            var nativeConversion = spec.TryGetProperty("real_conversions", out var rc) && rc.GetBoolean();
            var api = SessionImportConversionFixture.Create(events, conversion == "fail");
            var storageBound = spec.TryGetProperty("real_storage", out var rs) && rs.GetBoolean();
            var stored = new SacredSettingsDocument();
            if (storageBound)
            {
                var content = Convert.FromHexString(spec.GetProperty("settings_text").GetString()!); var cursor = 0;
                WorldSettingsFileLoader.Load(fileDefinition, stored, fileDefinition.FileName, fileDefinition.KeyDelimiters, new((path, mode) =>
                {
                    events.Add(["file-open", Hex(path.Span), Hex(mode.Span)]); return 0x76543210;
                }, (handle, capacity) =>
                {
                    if (cursor == content.Length) { events.Add(["file-read", capacity, null!]); return null; }
                    var end = Math.Min(content.Length, cursor + capacity - 1); var newline = Array.IndexOf(content, (byte)'\n', cursor, end - cursor);
                    if (newline >= 0) end = newline + 1;
                    var chunk = content.AsMemory(cursor, end - cursor); cursor = end;
                    events.Add(["file-read", capacity, Hex(chunk.Span)]); return chunk;
                }, handle => events.Add(["file-close", handle])));
            }
            var controllerBytes = new byte[0x4000]; var controller = Memory(controllerBytes);
            var addressBytes = new byte[0x40]; var manager = Memory(addressBytes);
            var connectionBytes = new[] { Enumerable.Repeat((byte)0xa5, 0x40468).ToArray(), Enumerable.Repeat((byte)0xa5, 0x40468).ToArray() };
            var connections = connectionBytes.Select(Memory).ToArray();
            uint current = spec.TryGetProperty("present", out var present) && !present.GetBoolean() ? 0u : 0x6010000u;
            byte flag = spec.TryGetProperty("flag", out var aseFlag) ? aseFlag.GetByte() : (byte)1;
            var ansi = new Dictionary<uint, ReadOnlyMemory<byte>>(); var wide = new Dictionary<uint, ReadOnlyMemory<char>>();
            foreach (uint at in new uint[] { 0x39fc, 0x3a08, 0x39e4 }) ansi[at] = "old"u8.ToArray();
            foreach (uint at in new uint[] { 0x3a14, 0x39f0, 0x3840, 0x3834, 0x3828 }) wide[at] = "old".ToCharArray();
            var strings = new WorldSessionStringStorage(at => ansi[at], (at, value) => ansi[at] = value.ToArray(),
                at => wide[at], (at, value) => wide[at] = value.ToArray());
            manager.WriteWord(0, 3); manager.WriteWord(4, 0x01020304); manager.WriteWord(8, 0x7f000001);
            manager.WriteWord(0xc, 0xff002c04); manager.WriteWord(0x10, 0x99999999); manager.WriteWord(0x14, 0x12345678);
            var services = new WorldSessionImportServices(controller, strings, key =>
                {
                    events.Add(["remove", Hex(key.Span)]); if (storageBound) stored.Values.Remove(key);
                },
                setting =>
                {
                    var name = Names[setting]; events.Add(["read", name]); Mutate("read-" + name);
                    if (spec.TryGetProperty("real_strings", out var real) && real.GetBoolean())
                        return WorldStringSetting.Read(stringDefinitions[Enum.Parse<SacredSessionStringSetting>(setting.ToString())], key =>
                        {
                            events.Add(["read-value", Hex(key.Span)]); Mutate("read-value-" + name);
                            if (storageBound) return stored.Values.Read(key);
                            return Convert.FromHexString(row.GetProperty("inputs").GetProperty(name).GetString()!);
                        }, (key, value) =>
                        {
                            events.Add(["write-string-default", Hex(key.Span), Hex(value.Span)]); Mutate("write-string-default-" + name);
                            if (storageBound) stored.Values.Write(key, value);
                        });
                    return Convert.FromHexString(row.GetProperty("inputs").GetProperty(name).GetString()!);
                }, setting =>
                {
                    var name = Names[setting]; events.Add(["read", name]); Mutate("read-" + name);
                    if (spec.TryGetProperty("real_integers", out var real) && real.GetBoolean())
                    {
                        ReadOnlyMemory<byte> ReadValue(ReadOnlyMemory<byte> key)
                        {
                            events.Add(["read-value", Hex(key.Span)]);
                            Mutate("read-value-" + name);
                            if (storageBound) return stored.Values.Read(key);
                            return Convert.FromHexString(spec.GetProperty("integer_value").GetString()!);
                        }
                        void WriteDefault(ReadOnlyMemory<byte> key, uint value)
                        {
                            events.Add(["write-default", Hex(key.Span), value]); Mutate("write-default-" + name);
                            if (storageBound) stored.Values.Write(key, SacredSettingsIntegerValue.Format(fileDefinition.IntegerFormat.Span, value));
                        }
                        int Parse(ReadOnlyMemory<byte> value) => SacredAnsiDecimalParser.Parse(value.Span, numeric.DefaultClasses.Span);
                        if (setting == WorldSessionSetting.ListenPort) return WorldListenPort.Read(numeric, new(ReadValue, WriteDefault, Parse));
                        var kind = setting switch
                        {
                            WorldSessionSetting.LobbyPort => SacredSessionIntegerSetting.LobbyPort,
                            WorldSessionSetting.Speed => SacredSessionIntegerSetting.Speed,
                            WorldSessionSetting.AsePort => SacredSessionIntegerSetting.AsePort,
                            _ => throw new InvalidOperationException("Unexpected integer getter.")
                        };
                        return WorldIntegerSetting.Read(integers[kind], ReadValue, WriteDefault, Parse);
                    }
                    return row.GetProperty("numbers").GetProperty(name).GetUInt32();
                }, value =>
                {
                    events.Add(["decode", Hex(value.Span)]);
                    if (nativeConversion) return WorldAnsiStringConversion.Decode(api, () => value);
                    if (conversion == "fail") return null;
                    var end = value.Span.IndexOf((byte)0);
                    return (ReadOnlyMemory<char>?)Encoding.Latin1.GetString(end < 0 ? value.Span : value.Span[..end]).ToCharArray();
                }, value =>
                {
                    events.Add(["encode", Hex(MemoryMarshal.AsBytes(value.Span))]);
                    if (nativeConversion) return WorldAnsiStringConversion.Encode(api, () => value).Value;
                    return conversion == "fail" ? Array.Empty<byte>() : Encoding.Latin1.GetBytes(value.Span.ToArray());
                }, manager, value => SacredAnsiIpv4Parser.Parse(value.Span, numeric.DefaultClasses.Span),
                (offset, value) => events.Add(["address-string", offset, Hex(value.Span)]),
                Current, () => flag, value => flag = value, (stage, value, number) =>
                {
                    object? argument = number.HasValue ? number.Value : stage is "selected" or "complete" or "lobbypwd" or "invalid-ase" ? null : Hex(value.Span);
                    events.Add(["log", stage, argument!]); Mutate("log-" + stage);
                });
            var selected = definition; var patches = row.GetProperty("definition_patches");
            if (patches.GetArrayLength() != 0)
            {
                var selectedImage = ListenPortChecks.Image(native.RootElement);
                foreach (var patch in patches.EnumerateArray())
                    Convert.FromHexString(patch.GetProperty("hex").GetString()!).CopyTo(selectedImage, patch.GetProperty("offset").GetInt32());
                selected = SacredSessionImportDefinitions.Read(selectedImage);
            }
            string? error = null;
            try { WorldSessionSettingsImport.Apply(selected, services); }
            catch (ArgumentOutOfRangeException) { error = "short-cdkey"; events.Add(["length-error"]); }
            catch (InvalidOperationException exception) when (exception.Message.Contains("missing current connection")) { error = "missing-current-connection"; }
            Check(error == row.GetProperty("error").GetString(), "native error boundary");
            Check(events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), "ordered services and values");
            Check(new[] { controller.ReadWord(0x3c), controller.ReadWord(0x40) }.SequenceEqual(row.GetProperty("controller_words").EnumerateArray().Select(value => value.GetUInt32())), "controller integers");
            Check(ansi.All(pair => Hex(pair.Value.Span) == row.GetProperty("strings").GetProperty(pair.Key.ToString()).GetString()) &&
                wide.All(pair => Hex(MemoryMarshal.AsBytes(pair.Value.Span)) == row.GetProperty("strings").GetProperty(pair.Key.ToString()).GetString()), "all controller ANSI/wide fields");
            Check(connections.SelectMany(connection => new[] { connection.ReadWord(0x40414), connection.ReadWord(0x40460), connection.ReadWord(0x40464) })
                .SequenceEqual(row.GetProperty("connections").EnumerateArray().SelectMany(array => array.EnumerateArray().Select(value => value.GetUInt32()))), "both connection instances");
            Check(current == row.GetProperty("current").GetUInt32() && flag == row.GetProperty("flag").GetByte(), "current connection and raw ASE flag");
            Check(Hex(addressBytes.AsSpan(0, 0x18)) == row.GetProperty("address").GetString(), "IP manager composition");
            if (storageBound)
                Check(SettingsStorageChecks.Snapshot(stored.Values).SequenceEqual(row.GetProperty("settings_entries").EnumerateArray().Select(pair => pair[0].GetString() + ":" + pair[1].GetString())), "complete loader/importer settings lifetime");

            WorldControlMemory? Current() => current == 0 ? null : connections[current == 0x6010000 ? 0 : 1];
            void Mutate(string stage)
            {
                if (mutation == stage + "-swap") current = 0x6060000;
                if (mutation == stage + "-null") current = 0;
                if (mutation == stage + "-ase-off") flag = 0;
                if (mutation == stage + "-ase-on") flag = 0x80;
                if (mutation == stage + "-invalidate") Current()!.WriteWord(0x40460, 0);
                if (mutation == stage + "-controller") controller.WriteWord(0x40, 777);
            }
            void Check(bool pass, string why)
            {
                if (!pass) throw new Exception($"Session settings {spec}: {why}"); checks++;
            }
        }
        return checks;
    }

    private static string Hex(ReadOnlySpan<byte> value) => Convert.ToHexString(value).ToLowerInvariant();
    private static WorldControlMemory Memory(byte[] bytes) => new(at => bytes[at], (at, value) => bytes[at] = value,
        at => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)at)),
        (at, value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)at), value),
        (at, value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan((int)at), value));
}
