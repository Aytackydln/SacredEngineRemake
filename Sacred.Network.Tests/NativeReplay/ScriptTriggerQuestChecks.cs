using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

internal static class ScriptTriggerQuestChecks
{
    public static int Run(string root)
    {
        using var fixture=Read(root);var checks=0;
        foreach(var row in fixture.RootElement.GetProperty("reset_cases").EnumerateArray())
            Check(new WorldScriptQuestJournal().GetIndices().AsSpan().SequenceEqual(Bytes(row,"indices_hex")),"native fresh index-pair initialization");
        foreach(var row in fixture.RootElement.GetProperty("function_cases").EnumerateArray())
        {
            var active=row.GetProperty("active").GetBoolean();var gate=row.GetProperty("gate").GetInt32();
            var quests=Quests(Quest(0,0).Concat(Quest(7,active?1u:0)).ToArray());
            var context=new WorldScriptExecutionContext(new(),new[]{new SacredScriptFunction("",0,0,-1,0,0),
                new(row.GetProperty("name").GetString()!,0,0,gate,0,row.GetProperty("deleted").GetUInt32())},ReadOnlyMemory<byte>.Empty,true){Quests=quests};
            var actual=WorldScriptEmptyFunctionResult.Invoke(context,row.GetProperty("index").GetInt32(),_=>{});
            Check(actual==(row.GetProperty("result").GetInt32()!=0),"actual empty-function return, bounds/deleted/prefix/quest gates");
        }
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var quests=Quests(Bytes(row,"before_hex"));KnownScratch(quests);
            var functions=Functions(Bytes(row,"functions_hex"));
            var context=new WorldScriptExecutionContext(new(),functions,ReadOnlyMemory<byte>.Empty,true){Quests=quests,LocalPlayerSlot=row.GetProperty("owner").GetUInt32(),HasActiveHero=false};
            var calls=new List<(int,uint,int)>();
            bool Predicate(int index)
            {
                calls.Add((index,quests.Get(1).Flags,quests.Journal.Count));
                return index==3?row.GetProperty("controlled_predicate_result").GetBoolean():WorldScriptEmptyFunctionResult.Invoke(context,index,_=>{});
            }
            WorldScriptQuestTrigger.Apply(context,new(0,Bytes(row,"command_hex")),true,Predicate,
                index=>calls.Add((index,quests.Get(1).Flags,quests.Journal.Count)));
            Check(Table(quests).AsSpan().SequenceEqual(Bytes(row,"after_hex")),"all selected, cached and untouched quest bytes");
            Check(calls.SequenceEqual(Calls(row)),"predicate/OnEnter order, flags and journal count at dispatch");
            Check(Journal(quests).AsSpan().SequenceEqual(Bytes(row,"journal_hex")),"complete native journal records, including runtime word");
            Check(quests.Journal.GetIndices().AsSpan().SequenceEqual(Bytes(row,"indices_hex")),"native local-player journal index pair");
            Check(quests.Scratch!.SelectMany(BitConverter.GetBytes).SequenceEqual(Bytes(row,"scratch_hex")),"native completion scratch clearing");
        }
        foreach(var row in fixture.RootElement.GetProperty("completion_cases").EnumerateArray())
        {
            var quests=Quests(Quest(0,0).Concat(Quest(row.GetProperty("number").GetInt32(),1)).ToArray(),
                WorldScriptQuestJournal.FromRuntime(Bytes(row,"before_journal_hex"),Bytes(row,"before_indices_hex")));
            KnownScratch(quests);var calls=new List<(int,uint,int)>();
            WorldScriptQuestTrigger.Enter(quests,1,row.GetProperty("owner").GetUInt32(),false,
                index=>calls.Add((index,quests.Get(1).Flags,quests.Journal.Count)));
            Check(Journal(quests).AsSpan().SequenceEqual(Bytes(row,"journal_hex")),"main/other category, prior-index carry, all journal bytes");
            Check(quests.Journal.GetIndices().AsSpan().SequenceEqual(Bytes(row,"indices_hex")),"owner low-word clamping and main/other index selection");
            Check(quests.Get(1).Flags==5 && calls.SequenceEqual(Calls(row)),"triggered flag precedes final callback");
        }
        var installed=fixture.RootElement.GetProperty("installed");
        var nativeQuests=Quests(Bytes(installed,"before_hex"));KnownScratch(nativeQuests);
        var records=installed.GetProperty("functions").EnumerateArray().ToArray();
        var installedFunctions=Enumerable.Range(0,records.Max(row=>row.GetProperty("index").GetInt32())+1).Select(_=>new SacredScriptFunction("",0,0,-1,0,0)).ToArray();
        foreach(var row in records) installedFunctions[row.GetProperty("index").GetInt32()]=Functions(Bytes(row,"hex"),int.MaxValue).Single();
        var installedContext=new WorldScriptExecutionContext(new(),installedFunctions,new byte[1705663],true){Quests=nativeQuests,LocalPlayerSlot=1,HasActiveHero=false};
        var installedCalls=new List<(int,uint,int)>();
        WorldScriptQuestTrigger.Apply(installedContext,new(0,Bytes(installed,"command_hex")),true,
            index=> { installedCalls.Add((index,nativeQuests.Get(573).Flags,nativeQuests.Journal.Count));return WorldScriptEmptyFunctionResult.Invoke(installedContext,index,_=>{}); },
            index=>installedCalls.Add((index,nativeQuests.Get(573).Flags,nativeQuests.Journal.Count)));
        Check(Table(nativeQuests).AsSpan().SequenceEqual(Bytes(installed,"after_hex")) && installedCalls.SequenceEqual(Calls(installed)),"original Tutorial table and actual empty callback completion prefix");
        Check(Journal(nativeQuests).AsSpan().SequenceEqual(Bytes(installed,"journal_hex")),"original Tutorial full journal record");
        // A failing final callback keeps native partial order, without completing startup.
        var q=Quest(7,1);BinaryPrimitives.WriteInt32LittleEndian(q.AsSpan(268),1);BinaryPrimitives.WriteInt32LittleEndian(q.AsSpan(272),2);
        var failureQuests=Quests(Quest(0,0).Concat(q).ToArray());KnownScratch(failureQuests);
        var failed=new WorldScriptExecutionContext(new(),new[]{new SacredScriptFunction("",0,0,-1,0,0),new("predicate",0,0,7,0,0),new("final",0,4,7,0,0)},Convert.FromHexString("e7030400"),true)
            {Quests=failureQuests,LocalPlayerSlot=1,HasActiveHero=false};
        try {new WorldScriptExecutor(failed).ExecuteStartup(Convert.FromHexString("140009000b07000000"));throw new Exception("Failing final callback accepted");}
        catch(WorldScriptExecutionFailure){Check(failed.IsInvalidated && !failed.StartupCompleted && failed.ExecutedCommands==0 && failureQuests.Get(1).Flags==5 && failureQuests.Journal.Count==1,"failed OnEnter leaves journal/flag changes private and invalidates executor");}
        return checks;
        void Check(bool pass,string why){if(!pass)throw new Exception("TriggerQuest: "+why);checks++;}
    }
    public static byte[] WireExample(string root)
    {
        using var fixture=Read(root);var journal=new WorldScriptQuestJournal();journal.AppendTriggerEntry(1,1);
        if(!journal.GetRecord(0).AsSpan().SequenceEqual(Bytes(fixture.RootElement.GetProperty("installed"),"journal_hex")))throw new Exception("Uncompared Tutorial journal");
        return BitConverter.GetBytes(0u).Concat(journal.GetWireRecord(0)).ToArray();
    }
    private static void KnownScratch(WorldScriptQuests quests)=>quests.Execute(new(0,Convert.FromHexString("15000400")),true,_=>{});
    private static byte[] Quest(int number,uint flags){var bytes=new byte[292];BinaryPrimitives.WriteInt32LittleEndian(bytes,number);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(272),2);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(288),flags);return bytes;}
    private static WorldScriptQuests Quests(byte[] bytes,WorldScriptQuestJournal? journal=null)=>new(bytes.Chunk(292).Select(row=>SacredScriptQuestDefinition.FromPayload(row))){Journal=journal??new()};
    private static byte[] Table(WorldScriptQuests quests)=>Enumerable.Range(0,quests.Count).SelectMany(index=>quests.Get(index).ToPayload()).ToArray();
    private static byte[] Journal(WorldScriptQuests quests)=>Enumerable.Range(0,quests.Journal.Count).SelectMany(quests.Journal.GetRecord).ToArray();
    private static SacredScriptFunction[] Functions(byte[] bytes,int codeLength=0)=>SacredScriptFunctionReader.Read(BitConverter.GetBytes(bytes.Length/84).Concat(bytes).ToArray(),codeLength).ToArray();
    private static IEnumerable<(int,uint,int)> Calls(JsonElement row)=>row.GetProperty("callbacks").EnumerateArray().Select(call=>(call.GetProperty("index").GetInt32(),call.GetProperty("flags").GetUInt32(),call.GetProperty("journal_count").GetInt32()));
    private static byte[] Bytes(JsonElement row,string property)=>Convert.FromHexString(row.GetProperty(property).GetString()!);
    private static JsonDocument Read(string root)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-script-trigger-quest.json")));
}
