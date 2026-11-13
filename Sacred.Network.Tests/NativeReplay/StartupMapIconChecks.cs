using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.GameRes;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;

internal static class StartupMapIconChecks
{
    public static int Run(string root)
    {
        using var fixture = Read(root);
        var keyx = new byte[256 + 768];
        BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(256 + 0x24), 1);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256 + 0x3c), SacredWorldCoordinates.TileCenterToWorld(52 * 64) - 25);
        BinaryPrimitives.WriteInt32LittleEndian(keyx.AsSpan(256 + 0x40), SacredWorldCoordinates.TileCenterToWorld(41 * 64) - 25);
        var grid = SacredSectorIndex.FromPayload(keyx).CreateGrid();
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var strings = new Dictionary<uint, string>();
            if (row.GetProperty("resource_exists").GetBoolean())
                foreach (var id in row.GetProperty("resource_queries").EnumerateArray()) strings[id.GetUInt32()] = "label";
            var icons = new WorldScriptMapIcons(grid, new GameResStore(strings.ToFrozenDictionary()));
            var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true) { MapIcons = icons };
            new WorldScriptExecutor(context).ExecuteStartup(Convert.FromHexString(row.GetProperty("command_hex").GetString()!));
            var expected = Canonical(row.GetProperty("row_hex").GetString()!);
            if (icons.Records.Count != (expected.Length == 0 ? 0 : 1) || context.ExecutedCommands != 1 || !context.StartupCompleted)
                throw new Exception("SetMapIcon native append count/startup execution differs.");
            checks++;
            if (expected.Length != 0)
            {
                if (!icons.Records.Single().ToPayload().AsSpan().SequenceEqual(expected))
                    throw new Exception("SetMapIcon native record fields differ outside alignment padding.");
                checks++;
            }
        }
        return checks;
    }
    public static byte[] WireExample(string root)
    {
        using var fixture = Read(root);
        var row = fixture.RootElement.GetProperty("cases").EnumerateArray().First(value =>
            value.GetProperty("fill").GetInt32() == 0 && value.GetProperty("resource_exists").GetBoolean() &&
            value.GetProperty("name").GetString() == "res:CITY_SCHOENBLICK_1");
        return Canonical(row.GetProperty("row_hex").GetString()!);
    }
    private static byte[] Canonical(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        if (bytes.Length != 0) foreach (var index in new[] { 2, 3, 13, 14, 15, 18, 19 }) bytes[index] = 0;
        return bytes;
    }
    private static JsonDocument Read(string root) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
        "docs/game-server/evidence/native-startup-map-icons.json")));
}
