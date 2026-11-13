using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class ScriptVariableEqualityChecks
{
    public static int Run(string root)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-script-variable-equality.json")));
        var checks=0;
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var variables=new WorldScriptVariables();
            variables.Import(row.GetProperty("variables").EnumerateArray().Select(value=>GoldScriptVariableRecord.FromPayload(Convert.FromHexString(value.GetString()!))));
            var command=new SacredScriptCommand(0,Convert.FromHexString(row.GetProperty("command_hex").GetString()!));
            Check(SacredScriptConditionReader.Evaluate(command,true,variables.Find)==row.GetProperty("result").GetBoolean(),"actual native equality/missing/duplicate lookup");
        }
        foreach(var row in fixture.RootElement.GetProperty("flows").EnumerateArray())
        {
            var random=new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var draws=0;var visited=new List<int>();
            var context=new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,true,()=>{draws++;return random.Next();})
                {BeforeCommand=command=>{if(command.Opcode is not (58 or 59 or 66))visited.Add(command.FileOffset);}};
            new WorldScriptExecutor(context).ExecuteStartup(Convert.FromHexString(row.GetProperty("code_hex").GetString()!));
            Check(visited.SequenceEqual(row.GetProperty("visited_offsets").EnumerateArray().Select(value=>value.GetInt32())),"actual random-variable IF/ELSEIF branch route");
            Check(context.Variables.Records.SelectMany(value=>value.ToPayload()).SequenceEqual(Convert.FromHexString(row.GetProperty("variables_hex").GetString()!)),"complete combined native variable table");
            Check(context.VariableGlobals.Apply(new int[150]).SequenceEqual(row.GetProperty("globals").EnumerateArray().Select(value=>value.GetInt32())),"combined native globals");
            Check(random.State==row.GetProperty("rng_after").GetUInt32()&&draws==row.GetProperty("draws").GetInt32()&&context.StartupCompleted,"combined native RNG and command completion");
        }
        return checks;
        void Check(bool value,string why){if(!value)throw new Exception("Variable equality: "+why);checks++;}
    }
}
