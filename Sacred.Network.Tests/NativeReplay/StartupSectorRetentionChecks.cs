using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;

internal static class StartupSectorRetentionChecks
{
    public static int Run(string root)
    {
        var checks=0;
        void Check(bool value,string why){if(!value)throw new Exception("Sector retention: "+why);checks++;}
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-sector-retention.json")));
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var flags=row.GetProperty("flags").EnumerateArray().Select((value,index)=>(Id:(uint)index,Flags:value.GetUInt32())).ToDictionary(pair=>pair.Id,pair=>pair.Flags);
            var plan=WorldScriptSectorRetention.Plan(Enumerable.Range(0,row.GetProperty("count").GetInt32()).Select(value=>(uint)value).ToArray(),flags);
            Check(plan.Unload.SequenceEqual(row.GetProperty("unloads").EnumerateArray().Select(value=>value.GetUInt32())),"native unload order including fallback sweep");
            Check(plan.Retained.Append(127u).SequenceEqual(row.GetProperty("cache_after").EnumerateArray().Select(value=>value.GetUInt32())),"native cache swap/protection order and successful append");
        }
        var keyx=new byte[256+97*768];BinaryPrimitives.WriteUInt32LittleEndian(keyx.AsSpan(4),97);
        for(var i=0;i<97;i++)
        {
            var row=keyx.AsSpan(256+i*768,768);BinaryPrimitives.WriteUInt32LittleEndian(row[0x24..],(uint)i+1);
            BinaryPrimitives.WriteInt32LittleEndian(row[0x3c..],SacredWorldCoordinates.TileCenterToWorld(i*64)-25);
            BinaryPrimitives.WriteInt32LittleEndian(row[0x40..],SacredWorldCoordinates.TileCenterToWorld(0)-25);
        }
        var calls=new List<uint>();var evicted=new List<uint>();
        var tiles=new WorldScriptPositionTiles(SacredSectorIndex.FromPayload(keyx),(id,_)=>{calls.Add(id);return new byte[131072];},(_,_)=>{});
        tiles.Evicted+=evicted.Add;
        for(var i=0;i<97;i++)Check(tiles.GetTile(i*64,0).HasValue,"valid synthetic sector query");
        var order=WorldScriptSectorRetention.Plan(Enumerable.Range(1,96).Select(value=>(uint)value).ToArray(),Enumerable.Range(1,96).ToDictionary(value=>(uint)value,_=>0x10000u));
        Check(evicted.SequenceEqual(order.Unload)&&tiles.Retained.SequenceEqual(new uint[]{97}),"97th load executes native sweep");
        Check(tiles.GetTile(0,0).HasValue&&calls.Count==98&&tiles.Retained.SequenceEqual(new uint[]{97,1}),"evicted tile data reloads and appends");
        return checks;
    }
}
