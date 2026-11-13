using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

internal static class CreatureCreationModeChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-creature-placement.json")));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray().Where(row =>
            row.GetProperty("name").GetString()!.StartsWith("behavior_",StringComparison.Ordinal) &&
            (row.GetProperty("name").GetString()!.Contains("_64_",StringComparison.Ordinal) ||
            row.GetProperty("name").GetString()!.EndsWith("_64",StringComparison.Ordinal))))
        {
            var command = new SacredScriptCommand(0,Convert.FromHexString(row.GetProperty("command_hex").GetString()!));
            var creation = SacredScriptCreatureCreationReader.Read(command,_=>throw new Exception("Unexpected symbolic lookup."));
            var expected = row.GetProperty("result");
            if (creation.Behavior != expected.GetProperty("behavior").GetUInt32() ||
                creation.Aggression != expected.GetProperty("aggression").GetInt32() ||
                creation.HasExplicitBehavior != expected.GetProperty("explicit").GetBoolean() ||
                creation.ConstructionFlags != expected.GetProperty("constructionFlags").GetUInt32())
                throw new Exception("HorseMerchant/native behavior ordering, aggression, construction bits or explicit marker differs.");
            checks++;
            if (!SacredScriptOperandReader.Read(command).Any(value=>value.Kind==SacredScriptArgumentKind.CreatureHorseMerchant))
                throw new Exception("Native HorseMerchant command lost its payload-free operand.");
            checks++;
        }
        return checks;
    }
}
