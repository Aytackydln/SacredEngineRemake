using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class StartupMouseoverChecks
{
    public static byte[] WireExample(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-mouseovers.json")));
        return Convert.FromHexString(fixture.RootElement.GetProperty("authored")[0].GetProperty("pending_result_hex").GetString()!);
    }
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value,string why) { if (!value) throw new Exception("Startup mouseover: " + why); checks++; }
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-mouseovers.json")));
        byte[] Bytes(JsonElement row,string name) => Convert.FromHexString(row.GetProperty(name).GetString()!);
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray().Concat(fixture.RootElement.GetProperty("authored").EnumerateArray()))
        {
            var command = new SacredScriptCommand(0,Bytes(row,"command_hex"));
            var creation = WorldScriptMouseovers.Read(command,_ => row.GetProperty("symbol_id").GetUInt32(),_ => throw new Exception("Unexpected symbolic position"));
            var record = creation?.Patch;
            var pending = Bytes(row,"pending_hex"); var after = Bytes(row,"pending_result_hex");
            Check((record is null ? 0 : 88) == after.Length-pending.Length,"record gate");
            Check(after.AsSpan(0,pending.Length).SequenceEqual(pending),"pending retains old records");
            if (record is null) continue;
            Check(record.ToPayload().AsSpan().SequenceEqual(after.AsSpan(pending.Length)),"complete 88-byte native record");
            var active = new GoldMouseoverRegistry(); var before = Bytes(row,"initial_hex");
            for (var at=0;at<before.Length;at+=88) active.Register(GoldSectorScriptPatch.FromPayload(before.AsSpan(at,88)));
            active.Register(record);
            Check(active.Active.SelectMany(value => value.ToPayload()).SequenceEqual(Bytes(row,"active_hex")),"active duplicate/priority policy");
            var calls = row.GetProperty("tile_calls").GetArrayLength();
            Check(row.GetProperty("tile_flags").GetByte() == (row.GetProperty("tile_exists").GetBoolean() && calls != 0 ? 0x95 : 0x15),"native tile mark");
            var tile = new byte[64]; tile[30] = 0x15;
            (byte[] Bytes,int Offset)? Query(int x,int y,ushort level)
            {
                var native = row.GetProperty("tile_queries").EnumerateArray().Last();
                Check(unchecked((uint)x) == native.GetProperty("x").GetUInt32() && unchecked((uint)y) == native.GetProperty("y").GetUInt32(),"direct tile coordinates retain full inputs");
                if (creation!.Position.Z != 0)
                {
                    var floorQuery = row.GetProperty("tile_queries").EnumerateArray().Last(value => value.GetProperty("address").GetUInt32() == 0x6354D0);
                    Check(level == unchecked((ushort)floorQuery.GetProperty("surface").GetUInt32()),"floor selection retains low word");
                }
                return row.GetProperty("tile_exists").GetBoolean() ? (tile,0) : null;
            }
            WorldScriptMouseoverTileMarks.Mark(creation!, (x,y) => Query(x,y,0), Query);
            Check(tile[30] == row.GetProperty("tile_flags").GetByte(),"managed direct terrain flag matches native");
            var marked = new List<GoldSectorScriptPatch>();
            var world = new WorldScriptMouseovers(_ => row.GetProperty("symbol_id").GetUInt32(),(_,patch) => marked.Add(patch.Patch));
            var context = new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,true) { Mouseovers = world };
            var twice = command.Bytes.ToArray().Concat(command.Bytes.ToArray()).ToArray();
            new WorldScriptExecutor(context).ExecuteStartup(twice);
            Check(context.StartupCompleted && world.Pending.Count == 2 && world.Active.Active.Count == 1 && marked.Count == 2,"startup duplicates append pending and mark every time");
        }
        return checks;
    }
}
