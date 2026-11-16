using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

internal static class AssociatedQuestChecks
{
    public static int Run(string root)
    {
        using var native=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-associated-quests.json")));
        var checks=0;
        foreach(var row in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var quests=Restore(Bytes(row,"before_hex"));var calls=new List<(int,uint,uint,int)>();
            var reference=row.GetProperty("actor_reference").GetUInt32();
            void Callback(int index,uint actor)
            {
                calls.Add((index,actor,quests.Get(1).Flags,quests.Get(1).SetupFunction));
                if(row.GetProperty("reenter").GetBoolean())quests.ExecuteAssociated(new(0,Convert.FromHexString("150009000b07000000")),true,actor,Callback);
            }
            quests.ExecuteAssociated(new(0,Bytes(row,"command_hex")),true,reference,Callback);
            Check(Enumerable.Range(0,quests.Count).SelectMany(index=>quests.Get(index).ToPayload()).SequenceEqual(Bytes(row,"after_hex")),"all mutated and preserved native quest bytes");
            Check(calls.SequenceEqual(row.GetProperty("calls").EnumerateArray().Select(call=>(call.GetProperty("index").GetInt32(),call.GetProperty("actor_reference").GetUInt32(),call.GetProperty("flags").GetUInt32(),call.GetProperty("once").GetInt32()))),"actual callback creature identity, order, busy/one-shot state and supplied reentry");
            Check(quests.Scratch!.SequenceEqual(new uint[5]),"all native scratch words clear");
        }
        // Execute actual nested functions under startup scope, preserving the
        // compared activation boundary while supplying observable callback bodies.
        foreach(var fails in new[]{false,true})
        {
            var table=new byte[584];BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(292),7);
            BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(292+276),1);BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(292+268),2);
            var activate=Convert.FromHexString("150009000b07000000");
            var callback=fails?Convert.FromHexString("e7030400"):Convert.FromHexString("3e000400");
            var functions=new SacredScriptFunction[]{new("",0,0,-1,0,0),new("setup",activate.Length,4,-1,0,0),new("activation",activate.Length,4,-1,0,0),new("entry",0,activate.Length,-1,0,0)};
            var seen=new List<uint>();WorldScriptExecutionContext context=null!;WorldScriptExecutor executor=null!;var started=false;
            context=new(new(),functions,activate.Concat(callback).ToArray(),true)
            {
                Quests=Restore(table),
                BeforeCommand=command=>
                {
                    if(!started){started=true;executor.InvokeAssociatedCreature(3,17);}
                    else if(command.Opcode==SacredScriptControlOpcodes.Nop)seen.Add(context.AssociatedCreatureReference);
                }
            };
            executor=new(context);
            try {executor.ExecuteStartup(Convert.FromHexString("3e000400"));Check(!fails,"unsupported setup cannot complete");}
            catch(WorldScriptExecutionFailure){Check(fails,"supported setup cannot fail");}
            Check(context.AssociatedCreatureReference==0,"outer creature identity restores across nested activation and failure");
            if(fails)Check(context.IsInvalidated&&context.Quests.Get(1).Flags==9&&context.Quests.Get(1).SetupFunction==1,"callback failure preserves native partial order and invalidates run");
            else Check(context.StartupCompleted&&seen.SequenceEqual(new uint[]{17,17})&&context.Quests.Get(1).Flags==1&&context.Quests.Get(1).SetupFunction==0,"real executor forwards creature into setup/activation bodies");
        }
        return checks;
        void Check(bool pass,string why){if(!pass)throw new Exception("Associated activation: "+why);checks++;}
    }
    private static WorldScriptQuests Restore(byte[] bytes)=>new(bytes.Chunk(292).Select(row=>SacredScriptQuestDefinition.FromPayload(row)));
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
}
