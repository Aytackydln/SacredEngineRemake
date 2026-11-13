using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

internal static class StartupQuestChecks
{
    public static int Run(string root)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-quests.json")));
        var checks=0;
        void Check(bool value,string why){if(!value)throw new Exception("Startup quests: "+why);checks++;}
        var load=fixture.RootElement.GetProperty("load");var bytes=Bytes(load,"quests_hex");
        var offset=load.GetProperty("table_offset").GetInt32();var vectors=new byte[offset+bytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(vectors,(uint)load.GetProperty("function_count").GetInt32());
        BinaryPrimitives.WriteUInt32LittleEndian(vectors.AsSpan(offset-4),(uint)load.GetProperty("quest_count").GetInt32());bytes.CopyTo(vectors,offset);
        var loaded=SacredScriptQuestReader.Read(vectors);
        Check(loaded.Count==603&&Convert.ToHexStringLower(SHA256.HashData(loaded.SelectMany(row=>row.ToPayload()).ToArray()))==load.GetProperty("quests_sha256").GetString(),"native bulk-read record order/bytes");
        Check(loaded[9].Number==10250&&loaded[9].SetupFunction==3157&&loaded[9].ActivationFunction==3158&&loaded[9].Flags==0,"installed activation inputs");
        foreach(var length in new[]{0,3,offset-1,vectors.Length-1})
        {
            try{_=SacredScriptQuestReader.Read(vectors.AsSpan(0,length));Check(false,"truncated table accepted");}catch(InvalidDataException){checks++;}
        }
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before=Bytes(row,"before_hex");var quests=new WorldScriptQuests(before.Chunk(292).Select(b=>SacredScriptQuestDefinition.FromPayload(b)));
            var calls=new List<(int,uint,int)>();
            var command=new SacredScriptCommand(0,Bytes(row,"command_hex"));
            void InvokeCallback(int index)
            {
                calls.Add((index,quests.Get(1).Flags,quests.Get(1).SetupFunction));
                if(row.GetProperty("reenter").GetBoolean())quests.Execute(new(0,Convert.FromHexString("150009000b07000000")),true,InvokeCallback);
            }
            quests.Execute(command,true,InvokeCallback);
            Check(Enumerable.Range(0,quests.Count).SelectMany(index=>quests.Get(index).ToPayload()).SequenceEqual(Bytes(row,"after_hex")),"all mutated and preserved quest bytes");
            var expected=row.GetProperty("calls").EnumerateArray().Select(call=>(call.GetProperty("args")[0].GetInt32(),call.GetProperty("flags").GetUInt32(),call.GetProperty("once").GetInt32())).ToArray();
            Check(calls.SequenceEqual(expected),"callback order/arguments/busy flag/one-shot clear");
            Check(quests.Scratch is not null&&quests.Scratch.SequenceEqual(new uint[5]),"native cleared scratch");
            Check(quests.IsActive(7)==((quests.Get(1).Flags&1)!=0)&&!quests.IsActive(0)&&!quests.IsActive(99),"number-based quest gating and reserved slot zero");
        }
        foreach(var row in fixture.RootElement.GetProperty("gates").EnumerateArray())
        {
            var table=new WorldScriptQuests(Bytes(row,"quests_hex").Chunk(292).Select(b=>SacredScriptQuestDefinition.FromPayload(b)));
            var number=row.GetProperty("number").GetInt32();
            Check((number<0||table.IsActive(number))==row.GetProperty("allowed").GetBoolean(),"native function quest-number gate and duplicate/zero slots");
        }
        // Real executor callbacks must share quest gating and preserve reentry/one-shot order.
        var q=loaded[9].ToPayload();BinaryPrimitives.WriteInt32LittleEndian(q.AsSpan(0x114),1);BinaryPrimitives.WriteInt32LittleEndian(q.AsSpan(0x10c),2);
        var startup=Convert.FromHexString("150009000b0a280000");
        // Use an ordinary no-op command as observable callback work, and reenter the active quest.
        var body=Convert.FromHexString("3e000400").Concat(startup).ToArray();
        var definitions=new[]{new SacredScriptFunction("",0,0,-1,0,0),new("setup",0,body.Length,10250,0,0),new("activate",body.Length,4,10250,0,0)};
        var context=new WorldScriptExecutionContext(new(),definitions,body.Concat(Convert.FromHexString("3e000400")).ToArray(),true){Quests=new(new[]{loaded[0],SacredScriptQuestDefinition.FromPayload(q)})};
        new WorldScriptExecutor(context).ExecuteStartup(startup);
        Check(context.StartupCompleted&&context.ExecutedCommands==4&&context.Quests.Get(1).SetupFunction==0&&context.Quests.Get(1).Flags==1,"composed executor gated callbacks and reentry");
        var failed = new WorldScriptExecutionContext(new(),new[]{definitions[0],new SacredScriptFunction("fails",0,4,10250,0,0),new("activate",4,0,10250,0,0)},Convert.FromHexString("e7030400"),true)
            {Quests=new(new[]{loaded[0],SacredScriptQuestDefinition.FromPayload(q)})};
        try { new WorldScriptExecutor(failed).ExecuteStartup(startup); Check(false,"failed callback credited activation"); }
        catch(WorldScriptExecutionFailure) { Check(failed.IsInvalidated&&!failed.StartupCompleted&&failed.Quests.Get(1).Flags==9&&failed.Quests.Get(1).SetupFunction==1,"failed setup preserves partial native order and invalidates context"); }
        var zeroCallbacks = new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,true)
            {Quests=new(new[]{loaded[0]})};
        new WorldScriptExecutor(zeroCallbacks).ExecuteStartup(Convert.FromHexString("150009000b63000000"));
        Check(zeroCallbacks.StartupCompleted&&zeroCallbacks.Quests.Get(0).Flags==1,"reserved zero callbacks skip without requiring a function record");
        return checks;
    }
    private static byte[] Bytes(JsonElement row,string name)=>Convert.FromHexString(row.GetProperty(name).GetString()!);
}
