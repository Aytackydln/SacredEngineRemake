using Sacred.Core.GameBin.Balance;

namespace Sacred.Core.Runtime;

/// <summary>Gold 0x5658F0 health arithmetic. The owner gate, boss-factor selection,
/// active effects and mounted lookup belong to the calling actor service.</summary>
public static class SacredCreatureHealth
{
    public static int Calculate(ushort level, ushort baseStrength, ushort baseEndurance,
        ushort strength, ushort endurance, byte vitalityRank, SacredCreatureBalance balance,
        uint difficulty, bool hero, uint multiplayerPlayers = 0, float bossFactor = 1,
        bool runtimeBonus = false)
        => Calculate(SacredX87Precision.Double, level, baseStrength, baseEndurance, strength, endurance,
            vitalityRank, balance, difficulty, hero, multiplayerPlayers, bossFactor, runtimeBonus);

    public static int Calculate(SacredX87Precision precision, ushort level, ushort baseStrength, ushort baseEndurance,
        ushort strength, ushort endurance, byte vitalityRank, SacredCreatureBalance balance,
        uint difficulty, bool hero, uint multiplayerPlayers = 0, float bossFactor = 1,
        bool runtimeBonus = false)
    {
        if (precision is not (SacredX87Precision.Single or SacredX87Precision.Double or SacredX87Precision.Extended))
            throw new ArgumentOutOfRangeException(nameof(precision));
        var fp = new SacredResultArithmetic(precision);
        var growth = (int)level - 1;
        var basis = baseStrength + baseStrength * growth / 10 + baseEndurance + baseEndurance * growth / 10;
        // Both stores use FST, so the intermediates on the x87 stack retain
        // their precision while the later denominator is read back as float.
        var exponent = fp.Add(1.5, fp.Multiply(Math.Max(0, 60 - baseStrength - baseEndurance), 0.003996323328465223f));
        var ratio = fp.Divide(strength + endurance, (float)basis);
        var health = Truncate(fp.Multiply(fp.Multiply(Math.Pow(basis, exponent), ratio), 1d / 3.1d));
        if (vitalityRank != 0)
        {
            var curve = balance.Vitality;
            var denominator = fp.Add(fp.Divide(fp.Subtract(vitalityRank, 1), curve.Divisor), 1);
            var approach = fp.Subtract(1, fp.Divide(1, denominator));
            var amount = fp.Multiply(fp.Subtract(curve.Limit, curve.Base), approach);
            amount = fp.Add(fp.Add(amount, amount), curve.Base);
            health = Truncate(fp.Add(health, fp.Multiply(fp.Multiply(health, amount), 0.01f)));
        }
        if (bossFactor != 1) health = Truncate(fp.Multiply(health, bossFactor));
        if (runtimeBonus) health = unchecked(health * 104) / 100;
        if (health == 0) health = 100;
        if (!hero)
        {
            float factor = 1;
            var levels = balance.HealthLevels;
            for (var i = 0; i < levels.Count - 1; i++)
            {
                var a = levels[i];
                var b = levels[i + 1];
                if (i != levels.Count - 2 && (level < a.Level || level > b.Level)) continue;
                // Native deliberately multiplies by level rather than level-a.Level.
                var slope = fp.Divide(fp.Subtract(b.Factor, a.Factor), fp.Subtract(b.Level, a.Level));
                factor = (float)fp.Add(fp.Multiply(slope, level), a.Factor);
                break;
            }
            if (multiplayerPlayers > 1)
                factor = SacredCreatureMultiplayerHealth.Scale(factor, multiplayerPlayers, precision);
            var multiplier = fp.Multiply(factor, balance.HealthDifficulties[SacredCreatureProgressionRules.DifficultyIndex(difficulty)]);
            health = Truncate(fp.Multiply(multiplier, health));
        }
        return health;
    }

    private static int Truncate(double value) => unchecked((int)(long)value);
}
