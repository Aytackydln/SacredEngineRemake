using System.Text.Json;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Runtime;

internal static class CreatureInnateBonusChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-innate-bonuses.json")));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var values = Convert.FromHexString(row.GetProperty("before_hex").GetString()!);
            var expected = Convert.FromHexString(row.GetProperty("after_hex").GetString()!);
            var bonuses = row.GetProperty("bonuses").EnumerateArray()
                .Select(value => new SacredCreatureInnateBonus(value[0].GetUInt16(), value[1].GetByte(), value[2].GetByte())).ToArray();
            if (bonuses.Any(bonus => bonus.CreatureClass == 0 && bonus.SourceOperation is >= 23 and <= 36))
            {
                try
                {
                    SacredCreatureInnateBonuses.Apply(values, bonuses, row.GetProperty("unarmed").GetBoolean(), row.GetProperty("ranged").GetBoolean());
                    throw new Exception("Unresolved legacy innate power was accepted.");
                }
                catch (NotSupportedException)
                {
                    if (!values.SequenceEqual(Convert.FromHexString(row.GetProperty("before_hex").GetString()!)))
                        throw new Exception("Unsupported innate power partially changed results.");
                    checks++;
                    continue;
                }
            }
            SacredCreatureInnateBonuses.Apply(values, bonuses, row.GetProperty("unarmed").GetBoolean(), row.GetProperty("ranged").GetBoolean());
            if (!values.SequenceEqual(expected))
            {
                var at = Enumerable.Range(0, values.Length).First(i => values[i] != expected[i]);
                throw new Exception($"Native innate bonus differs at value byte {at}: {row}; actual {Convert.ToHexString(values)}.");
            }
            checks += values.Length;
        }
        return checks;
    }
}
