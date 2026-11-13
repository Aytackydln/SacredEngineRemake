using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

internal static class ScriptHeroPredicateChecks
{
    public static int Run(string root)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-script-hero-predicates.json")));
        var checks=0;var positiveControls=0;
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var command=new SacredScriptCommand(0,Convert.FromHexString(row.GetProperty("command_hex").GetString()!));
            var hasHero=row.TryGetProperty("hero_type",out var type)&&type.ValueKind!=JsonValueKind.Null;
            var context=new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,row.GetProperty("multiplayer").GetBoolean()){HasActiveHero=hasHero};
            var value=SacredScriptConditionReader.Evaluate(command,context.Multiplayer,_=>null)??WorldScriptHeroPredicates.Evaluate(context,command);
            if(command.Bytes.Span[4]==SacredScriptConditionTags.IsHero && hasHero)
            {
                Check(value is null,"present hero remains guarded pending original type resolution");
                if(row.GetProperty("result").GetBoolean())positiveControls++;
            }
            else Check(value==row.GetProperty("result").GetBoolean(),"actual absent-hero and multiplayer branch result");
        }
        Check(positiveControls>0,"native active-hero positive controls exercise the other branch");
        var unknown=new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,true);
        Check(WorldScriptHeroPredicates.Evaluate(unknown,new(0,Convert.FromHexString("3a000700527800"))) is null,"unknown active-hero state is not defaulted to absence");
        return checks;
        void Check(bool pass,string why){if(!pass)throw new Exception("Hero predicate: "+why);checks++;}
    }
}
