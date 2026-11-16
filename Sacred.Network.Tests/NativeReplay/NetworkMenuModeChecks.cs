using System.Buffers.Binary;
using System.Text.Json;
using Sacred.GameplayHost.Networking;

internal static class NetworkMenuModeChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-network-menu-mode.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var menu = Memory(new byte[0x180]); var controller = Memory(Enumerable.Repeat((byte)0xa5, 0x3ad0).ToArray());
            var other = Memory(Enumerable.Repeat((byte)0x5a, 0x3ad0).ToArray()); var page = Memory(new byte[0x400]);
            var value = row.GetProperty("value").GetUInt32(); var source = row.GetProperty("source").GetUInt32();
            var mutation = row.GetProperty("mutation").GetString(); var calls = 0; var events = new List<object[]>();
            var multiplayer = row.GetProperty("multiplayer").GetByte();
            menu.WriteWord(0x154, source); menu.WriteWord(0x158, value);
            for (uint index = 0; index < 7; index++) menu.WriteWord(0x15c + index * 4, index == 5 ? 0x6010000 : 0x6020000 + index * 0x1000);
            page.WriteWord(0x3b0, 0x6027000); page.WriteWord(0x390, 77); page.WriteByte(0x3cc, 0xa5); page.WriteByte(0x3cd, 0xa5);
            var services = new WorldNetworkMenuServices(menu, () => multiplayer, () =>
            {
                calls++; events.Add(["resolve", calls]);
                if (mutation == "resolve-mode") menu.WriteWord(0x154, 5);
                return mutation == "resolve-replace" && calls == 2 ? other : controller;
            }, _ => page, (address, bits, set) =>
            {
                events.Add(["bits", address, bits, set]);
                if (events.Count == 1 && mutation == "callback-selection") menu.WriteWord(0x158, 4);
                if ((events.Count == 1 || row.GetProperty("kind").GetString() == "visibility") && mutation == "callback-source") menu.WriteWord(0x154, 5);
            }, (address, argument) => events.Add(["visible", address, argument]), (address, argument) => events.Add(["list", address, argument]));
            switch (row.GetProperty("kind").GetString())
            {
                case "convert": WorldNetworkMenuMode.ConvertAfterChildUpdate(services); break;
                case "select": WorldNetworkMenuMode.Select(services, value); break;
                case "configure": WorldNetworkMenuMode.ConfigurePage(services, 0x6010000, value); break;
                case "visibility": WorldNetworkMenuMode.SetVisibility(services, value, argument => events.Add(["baseVisibility", argument])); break;
                case "entry": WorldNetworkMenuEntry.Begin(services, value, () =>
                    {
                        events.Add(["credentials"]);
                        if (mutation == "credentials-mode") menu.WriteWord(0x154, 2);
                    }, (target, mode) =>
                    {
                        events.Add(["initialize", ReferenceEquals(target, controller) ? 0x6004000u : 0x6008000u, mode]);
                        if (mutation == "initialize-mode") menu.WriteWord(0x154, 3);
                    }); break;
                case "frame": WorldNetworkMenuFrame.Update(new(services, 0x6000000,
                    argument =>
                    {
                        events.Add(["base", argument]);
                        if (mutation == "base-source") menu.WriteWord(0x154, 6);
                        if (mutation == "base-multiplayer") multiplayer = 1;
                    },
                    (address, argument, special) =>
                    {
                        events.Add(["child", address, argument, special]);
                        if (mutation == "child-source") menu.WriteWord(0x154, 6);
                        if (mutation == "child-multiplayer") multiplayer = 0;
                    },
                    request =>
                    {
                        if (request.Target != "UI_MAINMENU") throw new Exception("Unexpected menu target.");
                        events.Add(["ui", request.EventId, request.Mode, request.NativeWord0C!.Value, request.Action]);
                        if (mutation == "ui-source") menu.WriteWord(0x154, 3);
                    }), value); break;
            }
            Check(events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())), "ordered services/current rereads");
            Check(menu.ReadWord(0x154) == row.GetProperty("source_after").GetUInt32() && menu.ReadWord(0x158) == row.GetProperty("selection_after").GetUInt32(), "source and selected page");
            Check(controller.ReadByte(0x381b) == row.GetProperty("controller_mode").GetByte() && other.ReadByte(0x381b) == row.GetProperty("other_mode").GetByte(), "current controller mode provenance");
            Check(Convert.ToHexString(new[] { page.ReadByte(0x3cc), page.ReadByte(0x3cd) }).Equals(row.GetProperty("page_flags").GetString(), StringComparison.OrdinalIgnoreCase), "page configuration");
            Check(Hex(controller) == row.GetProperty("controller_flags").GetString() && Hex(other) == row.GetProperty("other_flags").GetString(), "current controller flags after initializer");
            void Check(bool pass, string why) { if (!pass) throw new Exception($"Menu {row.GetProperty("kind")} {value}/{source} {mutation}: {why}"); checks++; }
        }
        return checks;
    }
    private static string Hex(WorldControlMemory memory) => Convert.ToHexString(new[] { memory.ReadByte(0x3819), memory.ReadByte(0x381a), memory.ReadByte(0x381b) }).ToLowerInvariant();
    private static WorldControlMemory Memory(byte[] bytes) => new(at => bytes[at], (at, value) => bytes[at] = value,
        at => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)at)),
        (at, value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)at), value),
        (at, value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan((int)at), value));
}
