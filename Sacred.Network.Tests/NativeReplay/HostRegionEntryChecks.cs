using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class HostRegionEntryChecks
{
    public static int Run(string root)
    {
        using var proof=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-host-region-entry.json")));
        var checks=0;
        var region=proof.RootElement.GetProperty("mapped_region").GetUInt32();
        Check(proof.RootElement.GetProperty("initial_current").GetUInt32()==uint.MaxValue,"original binary first-world current-region sentinel");
        foreach(var row in proof.RootElement.GetProperty("cases").EnumerateArray())
        {
            var lifecycle=new WorldScriptRegionInitialization(new Dictionary<uint,(uint,uint)>{[region]=(region,row.GetProperty("alive").GetBoolean()?0x101u:1u)})
                {CurrentRegion=0xaabbccdd};
            var state=new WorldScriptRegionEntry(lifecycle){EnteredRegion=row.GetProperty("old_current").GetUInt32()};
            uint? cached=row.GetProperty("cached").ValueKind==JsonValueKind.Null ? null : row.GetProperty("cached").GetUInt32();
            var calls=new List<ushort>();
            _=state.TryEnter(region,row.GetProperty("present").GetBoolean(),_=>cached,_=>2,index=>
            {
                calls.Add(index);
                Check(state.IsEntering && lifecycle.CurrentRegion==region && lifecycle.Records[region].Tag==1,"native callback scope before alive mutation");
                return true;
            });
            Check(calls.SequenceEqual(row.GetProperty("events").EnumerateArray().Select(value=>value[0].GetUInt16())),"native packed high/low calls and no-op gates");
            Check(lifecycle.Records[region].Tag==row.GetProperty("tag").GetUInt32(),"native record byte+5 only");
            Check(lifecycle.CurrentRegion==row.GetProperty("temporary").GetUInt32() && state.EnteredRegion==row.GetProperty("current").GetUInt32(),"native temporary/current region order");
            Check(!state.IsEntering && !state.IsPending,"completed native scope");
        }
        var pendingMap=new WorldScriptRegionInitialization(new Dictionary<uint,(uint,uint)>{[21]=(21,1)});
        var pending=new WorldScriptRegionEntry(pendingMap);var delivered=new List<ushort>();
        Check(pending.TryEnter(21,true,_=>0x10002,_=>-1,index=>{delivered.Add(index);return index==1;}) is null && pending.IsPending && pendingMap.Records[21].Tag==1,
            "pending second callback cannot mark the region alive");
        Check(pending.TryEnter(21,true,_=>throw new Exception("Repeated selection"),_=>throw new Exception("Repeated lookup"),index=>{delivered.Add(index);return true;})==true &&
            delivered.SequenceEqual(new ushort[]{1,2,2}) && pendingMap.Records[21].Tag==0x101 && pending.EnteredRegion==uint.MaxValue,
            "resume second callback only; scan existing alive entries before marking new one");
        return checks;
        void Check(bool condition,string reason){if(!condition)throw new Exception("Host EnterRegion: "+reason);checks++;}
    }
}
