using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.GameplayHost.Scripting;

internal static class ScriptInitialGlobalChecks
{
    public static int Run(string root)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-script-initial-globals.json")));
        var json=fixture.RootElement;
        var image=Bytes(json,"parameter_code_hex").Concat(new byte[16]).Concat(Bytes(json,"clear_code_hex")).ToArray();
        var definitions=SacredScriptInitializationDefinitions.Read(image);
        var alternative=SacredScriptInitializationDefinitions.Read(Bytes(json,"parameter_code_hex").Concat(new byte[16]).Concat(Bytes(json,"alternative_clear_code_hex")).ToArray());
        var checks=0;
        foreach(var row in json.GetProperty("cases").EnumerateArray())
        {
            var current=row.GetProperty("variant").GetInt32()==0?definitions:alternative;
            Compare(current.CreateParameterBlock(),Bytes(row,"initial_hex"),"complete reset parameter bytes");
            Compare(current.CreateVariableGlobals().SelectMany(BitConverter.GetBytes).ToArray(),Bytes(row,"globals_hex"),"separate 150-word zero initialization");
            Compare(current.CreateRegionReplacements(),Bytes(row,"replacement_hex"),"initial regional replacement storage");
            var context=new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,true)
            {
                StartupParameters=new(current.CreateParameterBlock()),
                VariableGlobals=new(current.CreateVariableGlobals())
            };
            new WorldScriptExecutor(context).ExecuteStartup(Bytes(row,"command_hex"));
            Check(context.StartupCompleted && context.ExecutedCommands==1,"complete actual parameter handler");
            Compare(context.StartupParameters.ToBankBlock(),Bytes(row,"after_hex"),"prefix overwrite preserves reset defaults");
            Compare(context.StartupParameters.ToBankBlock(),Bytes(row,"bank_hex"),"all original fixed-bank writer bytes");
        }
        try { _=new WorldScriptStartupParameters().ToBankBlock();throw new Exception("Unknown block serialized"); }
        catch(InvalidDataException) { Check(true,"missing provenance stays guarded"); }
        var globals=new WorldScriptVariableGlobals(definitions.CreateVariableGlobals());
        Check(globals.Apply().All(value=>value==0),"initialized empty global mutation sequence");
        Reject(image.Concat(image).ToArray());Reject([]);
        return checks;
        void Reject(byte[] bytes)
        {
            try { _=SacredScriptInitializationDefinitions.Read(bytes); }
            catch(InvalidDataException) { Check(true,"missing or ambiguous reset definition rejected");return; }
            throw new Exception("Invalid native reset accepted");
        }
        void Compare(byte[] actual,byte[] expected,string why)
        {
            Check(actual.Length==expected.Length,why+" length");
            for(var i=0;i<actual.Length;i++)Check(actual[i]==expected[i],why+$" at {i:X}");
        }
        void Check(bool pass,string why) { if(!pass)throw new Exception("Script initial globals: "+why);checks++; }
    }
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
}
