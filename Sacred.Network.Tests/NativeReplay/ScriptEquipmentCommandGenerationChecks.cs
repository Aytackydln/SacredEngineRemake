using System.Text.Json;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Scripting;

internal static class ScriptEquipmentCommandGenerationChecks
{
    public static int Run(string root)
        => Run(root,"native-script-equipment-command-generation");
    public static int RunShoulder(string root) => Run(root,"native-script-shoulder-generation");
    public static int RunAccessories(string root) => Run(root,"native-script-armor-accessory-generation");
    private static int Run(string root,string fixtureName)
    {
        using var source = Read(fixtureName);
        using var ordinary = Read("native-equipment-factory");
        using var names = Read("native-equipment-names");
        var data = EquipmentGenerationFixture.Read(root, ordinary.RootElement) with { Naming = EquipmentNameChecks.ReadNaming(names.RootElement) };
        var balance = CreatureBalanceBinReader.Read(Convert.FromHexString(source.RootElement.GetProperty("balance_prefix_hex").GetString()!));
        var checks = 0;
        foreach (var row in source.RootElement.GetProperty("conversions").EnumerateArray())
            Check(SacredCreatureProgressionRules.BaseLevelForDifficulty(row.GetProperty("level").GetUInt32(),
                row.GetProperty("difficulty").GetUInt32(), balance, SacredX87Precision.Single) == row.GetProperty("result").GetUInt32(), "actual startup reverse-level conversion");
        foreach (var row in source.RootElement.GetProperty("cases").EnumerateArray())
        {
            var type = row.GetProperty("type").GetUInt32();
            var item = new WorldEquipmentInitialItem(new(new(54,type,2,0,0,0,0,0,0,0,1,0),true,true), data.Templates.GetInstance(type));
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32());
            var level = row.GetProperty("level").GetUInt16(); var difficulty = row.GetProperty("difficulty").GetUInt32();
            Check(SacredCreatureProgressionRules.BaseLevelForDifficulty(level,difficulty,balance,SacredX87Precision.Single) == row.GetProperty("converted_level").GetUInt32(), "EquipItem passes the converted level to generation");
            WorldScriptEquipmentCommandGeneration.Apply(item,level,difficulty,balance,data,random);
            Check(item.Values.AsSpan().SequenceEqual(Convert.FromHexString(row.GetProperty("result_hex").GetString()!)), "all generated item bytes");
            Check(item.Name is { } name && name.NativeReference == row.GetProperty("name_reference").GetUInt32() && item.Physical.Base.Word3C == name.NativeReference, "native generated name and object identity");
            Check(random.State == row.GetProperty("rng_after").GetUInt32(), "complete generation/naming RNG, without two leading CreateNPC draws");
            Check(item.Generated && item.Quality == row.GetProperty("quality").GetUInt32() && item.LegacyGenerationSelector == 0 &&
                row.GetProperty("caller_rng_calls").GetInt32() == 0, "fixed quality/legacy inputs and no caller RNG");
        }
        return checks;
        JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/"+name+".json")));
        void Check(bool pass,string reason) { if (!pass) throw new Exception("EquipItem generation: " + reason); checks++; }
    }
}
