using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.GameRes;
using Sacred.GameplayHost.Scripting;

internal static class ObjectSecondaryNameChecks
{
    public static int Run(string root)
    {
        using var source=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-object-secondary-name.json")));
        var checks=0;
        foreach(var row in source.RootElement.GetProperty("cases").EnumerateArray())
        {
            var command=new SacredScriptCommand(0,Convert.FromHexString(row.GetProperty("command_hex").GetString()!));
            var runtime=SacredScriptObjectCreationReader.Read(command,_=>throw new Exception("Unexpected symbolic position"));
            var resources = new WorldScriptItemResources(new(), GameResStore.Empty);
            Check(resources.ResolveSecondary(runtime.SecondaryName) == row.GetProperty("secondary_resource").GetUInt32(), "secondary numeric decoding without primary-name reservation semantics");
            Check(resources.Replica.Names.Count == 0 && resources.Replica.ObjectBindings.Count == 0, "secondary decoding does not mutate local names or bindings");
            Check(SacredScriptCreateObjectReader.TryRead(command,out var inspection,out _),"complete inspection operands");
            foreach(var value in new[]{runtime,inspection!})
            {
                Check((value.Name??"")==row.GetProperty("primary").GetString()&&(value.SecondaryName??"")==row.GetProperty("secondary").GetString(),"original two-slot name parsing, empty-primary promotion and later-name replacement");
                var p=row.GetProperty("position");
                Check(value.TypeId==row.GetProperty("type").GetUInt32()&&value.TilePosition==new SacredScriptPosition(p[0].GetInt32(),p[1].GetInt32(),p[2].GetInt32()),"original type/position after repeated names");
            }
        }
        return checks;
        void Check(bool pass,string why){if(!pass)throw new Exception("Secondary object name: "+why);checks++;}
    }
}
