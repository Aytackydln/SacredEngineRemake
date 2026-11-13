using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;

internal static class StartupHideoutChecks
{
    public static int Run(string root)
    {
        using var fixture=Read(root);var checks=0;
        void Check(bool value,string why){if(!value)throw new Exception("Startup hideouts: "+why);checks++;}
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var definitions=new WorldScriptDefinitions();definitions.Import(Bytes(row,"initial_hex").Chunk(100).Select(bytes=>WorldScriptDefinition.FromPayload(bytes)));
            var random=new SacredRandomSequence(row.GetProperty("seed").GetUInt32());var calls=0;var marks=0;
            var flags=row.GetProperty("sector_flags").GetUInt32();var markByte=(byte)0x15;
            var mouseovers=new WorldScriptMouseovers(_=>0x80012345,(_,_)=>{marks++;markByte|=0x80;});
            var context=new WorldScriptExecutionContext(definitions,[],ReadOnlyMemory<byte>.Empty,true,()=>{calls++;return random.Next();})
                {Mouseovers=mouseovers,Hideouts=new(_=>0x80012345,(_,_)=>flags)};
            var variables=Bytes(row,"variables_hex");
            if(variables.Length!=0)context.Variables.Set(Encoding.Latin1.GetString(variables.AsSpan(0,Array.IndexOf(variables,(byte)0))),BinaryPrimitives.ReadInt32LittleEndian(variables.AsSpan(32)));
            new WorldScriptExecutor(context).ExecuteStartup(Bytes(row,"command_hex"));
            Check(context.StartupCompleted&&!context.IsInvalidated,"complete original command");
            Check(definitions.Records.SelectMany(value=>value.ToPayload()).SequenceEqual(Bytes(row,"definitions_hex")),"complete ordered definitions and stable pool erasure");
            Check(mouseovers.Pending.SelectMany(value=>value.ToPayload()).SequenceEqual(Bytes(row,"pending_hex")),"complete pending records in native order");
            Check(mouseovers.Active.Active.SelectMany(value=>value.ToPayload()).SequenceEqual(Bytes(row,"active_hex")),"complete active registry");
            Check(random.State==row.GetProperty("rng_after").GetUInt32()&&calls==row.GetProperty("rand_calls").GetInt32(),"native CRT RNG state and draw count");
            Check(marks==row.GetProperty("tile_calls").GetArrayLength()&&markByte==row.GetProperty("tile_flags").GetByte(),"runtime 0x400 direct-mark gate");
        }
        return checks;
    }
    public static byte[] WireExample(string root)
    {
        using var fixture=Read(root);
        var row=fixture.RootElement.GetProperty("cases").EnumerateArray().First(row=>row.GetProperty("pool_count").GetInt32()==5&&row.GetProperty("bound").ValueKind==JsonValueKind.Number&&row.GetProperty("bound").GetInt32()==3);
        return Bytes(row,"pending_hex")[..88];
    }
    private static JsonDocument Read(string root)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-hideouts.json")));
    private static byte[] Bytes(JsonElement row,string name)=>Convert.FromHexString(row.GetProperty(name).GetString()!);
}
