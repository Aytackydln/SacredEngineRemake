using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class ScriptResourceChecks
{
    public static int Run(string root)
    {
        var checks=0;
        void Check(bool pass,string name){if(!pass)throw new Exception("Script resources: "+name);checks++;}
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-script-resources.json")));
        foreach(var sample in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var world=new GoldWorldReplica();
            var initial=sample.GetProperty("initial").EnumerateArray().Select(row=>Convert.FromHexString(row.GetString()!)).ToArray();
            var bank=Bank(0x1B2,4,initial); world.Apply(GoldGameEventDecoder.Decode(bank));
            var immutable=world.WorldBanks.Current[0x1B2].ToPayload();
            var triggers=Enumerable.Range(0,8).Select(n=>new GoldWorldTriggerState((uint)n,0,0,0,0).ToPayload()).ToArray();
            world.Apply(GoldGameEventDecoder.Decode(Bank(0x1B1,6,triggers)));
            var name=sample.GetProperty("name").GetString()!;
            var resource=sample.GetProperty("resource");
            var wire=GoldScriptResourceCodec.Create(0,1,name,resource.ValueKind==JsonValueKind.Null?null:resource.GetUInt32());
            var copy=wire.Payload.ToArray(); var update=GoldGameEventDecoder.Decode(wire); wire.Payload.AsSpan().Fill(0x5a);
            world.Apply(update);
            var expected=sample.GetProperty("final").EnumerateArray().ToArray();
            Check(world.ScriptResources.Names.Count==expected.Length,"native ordered vector length");
            for(var i=0;i<expected.Length;i++)
            {
                var actual=world.ScriptResources.Names[i]; var row=expected[i];
                Check(actual.Name==row.GetProperty("name").GetString() && actual.TriggerReference==row.GetProperty("trigger").GetUInt32()
                    && actual.ResourceId==row.GetProperty("resource").GetUInt32(),"native name/trigger/resource identity and removal order");
                var bytes=actual.ToPayload(); bytes.AsSpan().Fill(0x5a);
                Check(world.ScriptResources.Names[i].Name==row.GetProperty("name").GetString(),"returned name bytes cannot mutate replica");
            }
            Check(world.WorldTriggers.Records[2].Referent==sample.GetProperty("trigger2_referent").GetUInt32(),"existing trigger receives native resource referent");
            Check(world.WorldBanks.Current[0x1B2].ToPayload().SequenceEqual(immutable),"incremental name update preserves retained bank bytes");
            Check(((GoldScriptResourceEvent)update).Message.Payload.SequenceEqual(copy),"decoded event owns payload/suffix bytes");
            world.Clear(); Check(world.ScriptResources.Names.Count==0 && world.ScriptResources.ObjectBindings.Count==0 && world.WorldTriggers.Records.Count==0,"clear removes session resource/trigger state");
        }
        foreach(var sample in fixture.RootElement.GetProperty("lookup").EnumerateArray())
        {
            var world=new GoldWorldReplica();var templates=new Dictionary<uint,uint>();
            foreach(var actor in sample.GetProperty("objects").EnumerateArray())
                templates[actor.GetProperty("type").GetUInt32()]=actor.GetProperty("template_resource").GetUInt32();
            world.ConfigureObjectResources(templates);
            foreach(var actor in sample.GetProperty("objects").EnumerateArray())
            {
                var state=new GoldObjectBaseState(actor.GetProperty("reference").GetUInt32(),actor.GetProperty("type").GetUInt32(),
                    actor.GetProperty("inactive").GetBoolean()?16u:0u,1,0,0,0,0,0,0,1,actor.GetProperty("resource").GetUInt32());
                world.Apply(GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16E,1,state.ToPayload())));
            }
            if(sample.GetProperty("mapped") is {ValueKind:JsonValueKind.Number} mapped)
                world.Apply(GoldGameEventDecoder.Decode(Bank(0x1B3,6,[Pair(8300,mapped.GetUInt32())])));
            var expected=sample.GetProperty("reference");
            Check(world.FindScriptObject(sample.GetProperty("resource").GetUInt32())==(expected.ValueKind==JsonValueKind.Null?null:expected.GetUInt32()),
                $"native lookup: resource {sample.GetProperty("resource")}, mapped {sample.GetProperty("mapped")}, expected {expected}, actual {world.FindScriptObject(sample.GetProperty("resource").GetUInt32())}");
        }
        var names=new GoldScriptResourceReplica();
        names.Apply(GoldWorldBankSnapshot.FromServerPayload(0x1B2,Bank(0x1B2,4,[Name("LRes:first",2,8300)]).Payload));
        foreach(var sample in fixture.RootElement.GetProperty("special").EnumerateArray())
        {
            var name=sample.GetProperty("name").GetString()!;var expected=sample.GetProperty("result").GetUInt32();
            // With create=false, native unknown and intentionally unnamed both return -1;
            // the managed read-only API distinguishes an unknown name requiring reservation.
            Check(names.FindLocalResource(name)==(name=="missing"?null:expected),"native local-resource prefix and special names");
        }
        names.Apply(GoldWorldBankSnapshot.FromServerPayload(0x1B2,new byte[40]));
        Check(names.Names.Count==1,"zero-count name snapshot retains native vector");
        names.Apply(GoldWorldBankSnapshot.FromServerPayload(0x1B3,Bank(0x1B3,6,[Pair(8300,91),Pair(8301,92)]).Payload));
        names.Apply(GoldWorldBankSnapshot.FromServerPayload(0x1B3,Bank(0x1B3,6,[Pair(8300,93)]).Payload));
        names.Apply(GoldWorldBankSnapshot.FromServerPayload(0x1B3,new byte[40]));
        Check(names.ObjectBindings.Count==2 && names.ObjectBindings[8300]==93 && names.ObjectBindings[8301]==92,"binding snapshots merge and zero count retains mappings");
        var tail=Name("lres:FIRST",2,8300); tail.AsSpan(12,50).Fill(0xab); tail[10]=0;
        names.Apply(GoldWorldBankSnapshot.FromServerPayload(0x1B2,Bank(0x1B2,4,[tail]).Payload));
        names.Apply(GoldScriptResourceCodec.Decode(GoldScriptResourceCodec.Create(0,1,"LRes:first",8301)));
        Check(names.Names[0].ToPayload().AsSpan(12,50).SequenceEqual(tail.AsSpan(12,50)),"native strcpy retains opaque name suffix bytes");
        foreach(var opcode in new ushort[]{0x1B6,0x1B7})
        foreach(var bytes in new[]{Array.Empty<byte>(),new byte[]{65},Enumerable.Repeat((byte)65,80).ToArray()})
        {
            try{GoldGameEventDecoder.Decode(new(opcode,0,1,0,bytes));throw new Exception("Malformed script name accepted");}
            catch(InvalidDataException){checks++;}
        }
        var suffixed=GoldScriptResourceCodec.Create(0,1,"name",123); suffixed=suffixed with{Payload=suffixed.Payload.Concat(new byte[]{9,8}).ToArray()};
        Check(GoldScriptResourceCodec.Decode(suffixed).Message.Payload.SequenceEqual(suffixed.Payload),"opaque packet suffix retained");
        var batch=new GoldWorldReplica();
        try{GoldGameDispatcher.Dispatch([GoldScriptResourceCodec.Create(0,1,"first",1),new(0x1B7,0,1,0,[1])],batch.Apply);throw new Exception("Invalid batch accepted");}
        catch(InvalidDataException){Check(batch.ScriptResources.Names.Count==0,"late malformed name prevents partial batch application");}
        Console.WriteLine($"PASS: {checks} script resource/name/trigger checks against native execution.");return checks;
    }
    public static byte[] Pair(uint key,uint value){var bytes=new byte[8];BinaryPrimitives.WriteUInt32LittleEndian(bytes,key);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4),value);return bytes;}
    public static byte[] Name(string name,uint trigger,uint resource)
    {
        var bytes=new byte[72];Encoding.Latin1.GetBytes(name).CopyTo(bytes,0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64),trigger);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(68),resource);return bytes;
    }
    public static GoldMessage Bank(ushort opcode,int section,byte[][] records)
    {
        var bytes=new byte[40+(opcode==0x1B1?GoldWorldBankGlobals.Size:0)+records.Sum(row=>row.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(section*4),(uint)records.Length);
        var cursor=40+(opcode==0x1B1?GoldWorldBankGlobals.Size:0);foreach(var row in records){row.CopyTo(bytes,cursor);cursor+=row.Length;}
        return new(opcode,0,1,0,bytes);
    }
}
