using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

internal static class CreatureBehavior2000Checks
{
    public static int Run(string root)
    {
        using var source=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-creature-behavior-2000.json")));
        var checks=0;
        foreach(var row in source.RootElement.GetProperty("cases").EnumerateArray())
        {
            var command=new SacredScriptCommand(0,Convert.FromHexString(row.GetProperty("command_hex").GetString()!));
            var actual=SacredScriptCreatureCreationReader.Read(command,_=>throw new Exception("Unexpected symbolic position"));
            var expected=row.GetProperty("result");
            Check(actual.Behavior==expected.GetProperty("behavior").GetUInt32(),"native OR/replace ordering");
            Check(actual.HasExplicitBehavior==expected.GetProperty("explicit").GetBoolean(),"native explicit-behavior gate");
            Check(actual.Aggression==expected.GetProperty("aggression").GetInt32(),"native aggression remains operand ordered");
            Check(actual.ConstructionFlags==expected.GetProperty("constructionFlags").GetUInt32(),"independent service modes");
        }
        return checks;
        void Check(bool pass,string why){if(!pass)throw new Exception("Behavior 0x2000: "+why);checks++;}
    }
}
