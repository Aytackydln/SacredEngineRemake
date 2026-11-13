using System.Collections.Frozen;
using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.GameRes;
using Sacred.GameplayHost.Scripting;

internal static class ScriptQuestBookChecks
{
    public static int Run(string root)
    {
        using var fixture=Read(root);var checks=0;
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before=Bytes(row,"before_hex");var journal=WorldScriptQuestJournal.FromRuntime(before,new WorldScriptQuestJournal().GetIndices());
            var context=Context(journal,row.GetProperty("resource_exists").GetBoolean());
            new WorldScriptExecutor(context).ExecuteStartup(Bytes(row,"command_hex"));
            var actual=Enumerable.Range(0,journal.Count).SelectMany(journal.GetRecord).ToArray();
            Check(actual.AsSpan().SequenceEqual(Bytes(row,"after_hex")),"all journal bytes, title/body mode, missing record/resource and full-slot handling");
            Check(context.StartupCompleted && context.ExecutedCommands==1 && journal.Count==before.Length/372,"no invented journal insertion or live send");
        }
        var chain=fixture.RootElement.GetProperty("chain");var retained=WorldScriptQuestJournal.FromRuntime(Bytes(chain,"before_hex"),new WorldScriptQuestJournal().GetIndices());
        foreach(var row in chain.GetProperty("steps").EnumerateArray())
        {
            var context=Context(retained,true);new WorldScriptExecutor(context).ExecuteStartup(Bytes(row,"command_hex"));
            Check(retained.GetRecord(0).AsSpan().SequenceEqual(Bytes(row,"after_hex")),"consecutive actual title/ten body appends and ignored eleventh");
        }
        var wire=retained.GetWireRecord(0);var expected=retained.GetRecord(0);
        Check(wire.AsSpan(0,12).SequenceEqual(expected.AsSpan(0,12)) && wire.AsSpan(12).SequenceEqual(expected.AsSpan(16)),"wire skips precisely the native runtime flag word");
        Array.Fill(wire,(byte)0xcc);Check(retained.GetRecord(0).AsSpan().SequenceEqual(expected),"journal exports own their bytes");
        return checks;
        void Check(bool pass,string why){if(!pass)throw new Exception("QuestBook: "+why);checks++;}
    }
    public static byte[] WireExample(string root)
    {
        using var fixture=Read(root);var chain=fixture.RootElement.GetProperty("chain");
        var journal=WorldScriptQuestJournal.FromRuntime(Bytes(chain,"before_hex"),new WorldScriptQuestJournal().GetIndices());
        foreach(var row in chain.GetProperty("steps").EnumerateArray().Take(2)) new WorldScriptExecutor(Context(journal,true)).ExecuteStartup(Bytes(row,"command_hex"));
        if(!journal.GetRecord(0).AsSpan().SequenceEqual(Bytes(chain.GetProperty("steps")[1],"after_hex")))throw new Exception("Uncompared QuestBook output");
        return BitConverter.GetBytes(0u).Concat(journal.GetWireRecord(0)).ToArray();
    }
    private static WorldScriptExecutionContext Context(WorldScriptQuestJournal journal,bool resourceExists)
    {
        var hash=SacredResourceHash.ComputeDefaultCLocale("LOG_MPSTART01"u8);
        var text=resourceExists?new GameResStore(new Dictionary<uint,string>{{hash,"present"}}.ToFrozenDictionary()):GameResStore.Empty;
        return new(new(),[],ReadOnlyMemory<byte>.Empty,true){Quests=new(Array.Empty<SacredScriptQuestDefinition>()){Journal=journal},ResourceText=text};
    }
    private static JsonDocument Read(string root)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-script-quest-book.json")));
    private static byte[] Bytes(JsonElement row,string property)=>Convert.FromHexString(row.GetProperty(property).GetString()!);
}
