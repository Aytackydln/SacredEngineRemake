using System.Collections.Frozen;
using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.GameRes;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;

internal static class ScriptSetRegionChecks
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,Lazy<byte[]>> Wire = new();
    public static byte[] WireExample(string root) => Wire.GetOrAdd(root,path=>new(()=>BuildWire(path))).Value.ToArray();
    private static byte[] BuildWire(string root)
    {
        byte[]? result=null;Run(root,bytes=>result??=bytes);return result!;
    }
    public static int Run(string root) => Run(root,null);
    private static int Run(string root,Action<byte[]>? collect)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-script-set-rg.json")));
        var json=fixture.RootElement;
        var sectors=SacredSectorIndex.FromPayload(Bytes(json,"keyx_hex")).CreateGrid();
        var resources=new GameResStore(json.GetProperty("resources").EnumerateObject().ToFrozenDictionary(p=>uint.Parse(p.Name),p=>p.Value.GetString()!));
        var checks=0;
        foreach(var row in json.GetProperty("cases").EnumerateArray())
        {
            var regions=new WorldScriptRegions(sectors,resources);
            var command=new SacredScriptCommand(0,Bytes(row,"command_hex"));
            var startup=row.GetProperty("startup").GetBoolean();
            var sequence=row.GetProperty("sequence_hex").EnumerateArray().Select(value=>Convert.FromHexString(value.GetString()!)).ToArray();
            foreach(var instruction in sequence) regions.Apply(new(0,instruction),startup,row.GetProperty("multiplayer").GetBoolean());
            var after=row.GetProperty("after");
            Check(regions.Records.Count==after.EnumerateObject().Count(),"all native keys");
            foreach(var group in after.EnumerateObject())
                Check(regions.Records[uint.Parse(group.Name)].SelectMany(r=>r.ToPayload()).SequenceEqual(Convert.FromHexString(group.Value.GetString()!)),"all native region bytes and duplicate order: "+row.GetProperty("command_hex").GetString()+" key="+group.Name+" actual="+Convert.ToHexString(regions.Records[uint.Parse(group.Name)].SelectMany(r=>r.ToPayload()).ToArray())+" expected="+group.Value.GetString());
            var bank=regions.WriteBankSection();
            Check(bank.Count==row.GetProperty("bank_count").GetUInt32() && bank.Bytes.SequenceEqual(Bytes(row,"bank_section_hex")),"native AVL enumeration and full bank-1 section-5 writer bytes");
            var publications=row.GetProperty("publications");
            Check(regions.Publications.Count==publications.GetArrayLength(),"native startup/live publication count");
            for(var i=0;i<regions.Publications.Count;i++)
            {
                var publication=publications[i];
                var expected=new[]{publication.GetProperty("mode").GetUInt32(),publication.GetProperty("first").GetUInt32(),publication.GetProperty("second").GetUInt32()}
                    .SelectMany(BitConverter.GetBytes).Concat(Bytes(publication,"record_hex")).ToArray();
                Check(regions.Publications[i].Payload.SequenceEqual(expected),"complete original producer payload and unsorted endpoint keys");
                collect?.Invoke(regions.Publications[i].Payload);
            }
            if(startup)
            {
                var controlled=new WorldScriptRegions(sectors,resources);
                var context=new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,row.GetProperty("multiplayer").GetBoolean()) { ScriptRegions=controlled };
                var repeated=sequence.SelectMany(value=>value).ToArray();
                new WorldScriptExecutor(context).ExecuteStartup(repeated);
                Check(context.StartupCompleted && context.ExecutedCommands==row.GetProperty("repeat").GetInt32(),"actual executor handler and no-op command credit");
                Check(controlled.Records.Count==regions.Records.Count && controlled.Records.All(p=>p.Value.SelectMany(r=>r.ToPayload()).SequenceEqual(regions.Records[p.Key].SelectMany(r=>r.ToPayload()))),"executor retains same private state");
            }
        }
        var missing=new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,true);
        try { new WorldScriptExecutor(missing).ExecuteStartup(Convert.FromHexString("25000400"));throw new Exception("Missing sector service accepted"); }
        catch(WorldScriptExecutionFailure) { Check(missing.IsInvalidated && missing.ExecutedCommands==0,"missing original services remain guarded"); }
        return checks;
        void Check(bool pass,string why) { if(!pass)throw new Exception("Native SetRG: "+why);checks++; }
    }
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
}
