using System.Text.Json;
using Sacred.GameplayHost.Networking;

internal static class WindowBitsChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-window-bits.json")));
        var checks = 0;
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var flags = row.GetProperty("flags").GetUInt32(); var events = new List<uint[]>();
            var window = new WorldControlMemory(_ => 0, (_, _) => { }, at => at == 0x10 ? flags : throw new Exception("Unexpected field."),
                (at, value) => { if (at != 0x10) throw new Exception("Unexpected field."); flags = value; }, (_, _) => { });
            WorldWindowBits.Apply(window, row.GetProperty("mask").GetUInt32(), row.GetProperty("set").GetBoolean(), argument =>
            {
                events.Add([argument, flags]);
                if (row.GetProperty("mutation").GetString() == "zero") flags = 0;
                if (row.GetProperty("mutation").GetString() == "pattern") flags = 0xa5a5a5a5;
            });
            if (flags != row.GetProperty("after").GetUInt32() || !events.Select(value => JsonSerializer.Serialize(value)).SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value => value.GetRawText())))
                throw new Exception("Window bits differ from original.");
            checks += 2;
        }
        return checks;
    }
}
