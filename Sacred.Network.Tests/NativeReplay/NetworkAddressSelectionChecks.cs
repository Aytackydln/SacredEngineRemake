using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.Runtime;

internal static class NetworkAddressSelectionChecks
{
    public static int Run(string root)
    {
        using var port = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-listen-port.json")));
        var definition = SacredListenPortDefinitions.Read(ListenPortChecks.Image(port.RootElement));
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-network-address-selection.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var bytes = new byte[0x40];
            var manager = new WorldControlMemory(at => bytes[at], (at, value) => bytes[at] = value,
                at => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)at)),
                (at, value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)at), value),
                (at, value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan((int)at), value));
            manager.WriteWord(0, row.GetProperty("count").GetUInt32()); manager.WriteWord(4, 0x01020304);
            manager.WriteWord(8, 0x7f000001); manager.WriteWord(0xc, 0xff002c04);
            manager.WriteWord(0x10, row.GetProperty("current").GetUInt32()); manager.WriteWord(0x14, 0x12345678);
            var input = Convert.FromHexString(row.GetProperty("input_hex").GetString()!);
            var mutation = row.GetProperty("mutation").GetString(); var events = new List<object[]>();
            WorldNetworkAddressSelection.Apply(new(manager, () => { events.Add(["read"]); return input; },
                value => SacredAnsiIpv4Parser.Parse(value.Span, definition.DefaultClasses.Span), (offset, value) =>
                {
                    events.Add(["string", offset, Convert.ToHexString(value.Span).ToLowerInvariant()]);
                    if (offset == 0x18 && mutation == "string-selected") manager.WriteWord(0x14, 0x87654321);
                }, (stage, value) =>
                {
                    events.Add(stage == "configured" ? new object[] { stage, Convert.ToHexString(value.Span).ToLowerInvariant() } : new object[] { stage });
                    if (stage == "configured" && mutation == "configured-current") manager.WriteWord(0x10, 0x01020304);
                    if (stage == "configured" && mutation == "configured-count") manager.WriteWord(0, 5);
                }));
            Check(events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), "service order and bytes");
            Check(bytes.AsSpan(0, 0x18).SequenceEqual(Convert.FromHexString(row.GetProperty("after").GetString()!)), "address-manager fields");
            Check(WorldNetworkAddressSelection.ReadSelected(manager) == row.GetProperty("selected_word").GetUInt32(), "selected word / roster source");
            void Check(bool pass, string why) { if (!pass) throw new Exception($"IP selection {row.GetProperty("input_hex")} {mutation}: {why}"); checks++; }
        }
        return checks;
    }
}
