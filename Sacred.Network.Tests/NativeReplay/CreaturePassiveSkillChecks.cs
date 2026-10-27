using System.Text.Json;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.Runtime;

internal static class CreaturePassiveSkillChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-passive-skills.json")));
        var json = fixture.RootElement;
        var tuning = CreatureSkillBalanceBinReader.Read(Convert.FromHexString(json.GetProperty("balance_prefix_hex").GetString()!));
        var checks = 0;
        foreach (var row in json.GetProperty("cases").EnumerateArray())
        {
            var input = row.GetProperty("inputs");
            var flags = input.GetProperty("predicates").EnumerateArray().Select(value => value.GetBoolean()).ToArray();
            var context = new SacredCreatureSkillContext(flags[0], flags[1], flags[2], flags[3], flags[4], flags[5],
                input.GetProperty("usage").GetUInt16(), input.GetProperty("offhand").GetBoolean(), input.GetProperty("offhand_usage10").GetBoolean());
            var values = Convert.FromHexString(row.GetProperty("before_hex").GetString()!);
            var expected = Convert.FromHexString(row.GetProperty("after_hex").GetString()!);
            var regeneration = SacredCreaturePassiveSkills.Apply(values, row.GetProperty("regeneration").GetUInt32(), 0, context, tuning);
            if (!values.SequenceEqual(expected) || regeneration != row.GetProperty("regeneration_after").GetUInt32())
            {
                var at = Enumerable.Range(0, values.Length).FirstOrDefault(i => values[i] != expected[i], -1);
                throw new Exception($"Native passive skills differ at {at}, regeneration {regeneration}: {row}; actual {Convert.ToHexString(values)}");
            }
            checks += values.Length + 1;
        }
        var guarded = new byte[SacredActorValuesLayout.Size];
        guarded[28] = 2; guarded[36] = 10;
        try { SacredCreaturePassiveSkills.Apply(guarded, 0, 1, default, tuning); throw new Exception("Learned combat arts silently ignored."); }
        catch (NotSupportedException) { checks++; }
        if (guarded[158] != 0) throw new Exception("Unsupported arts partially applied passive skills.");
        return checks;
    }
}
