using Sacred.Core.GameBin.Balance;

namespace Sacred.Core.Runtime;

/// <summary>Gold 0x5658F0 health arithmetic. The owner gate, boss-factor selection,
/// active effects and summoner lookup belong to the calling actor service.</summary>
public static class SacredCreatureHealth
{
    public static int Calculate(ushort level, ushort baseStrength, ushort baseEndurance,
        ushort strength, ushort endurance, byte vitalityRank, SacredCreatureBalance balance,
        uint difficulty, bool hero, uint multiplayerPlayers = 0, float bossFactor = 1,
        bool runtimeBonus = false)
    {
        var growth = (int)level - 1;
        var basis = baseStrength + baseStrength * growth / 10 + baseEndurance + baseEndurance * growth / 10;
        // Both stores use FST, so the intermediates on the x87 stack retain
        // their precision while the later denominator is read back as float.
        var exponent = 1.5d + Math.Max(0, 60 - baseStrength - baseEndurance) * (double)0.003996323328465223f;
        var health = Truncate(Math.Pow(basis, exponent) * ((double)(strength + endurance) / (float)basis) * (1d / 3.1d));
        if (vitalityRank != 0)
        {
            var curve = balance.Vitality;
            var amount = ((double)curve.Limit - curve.Base) *
                (1d - 1d / (((double)vitalityRank - 1d) / curve.Divisor + 1d));
            amount = amount + amount + curve.Base;
            health = Truncate(health + health * amount * (double)0.01f);
        }
        health = Truncate(health * (double)bossFactor);
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
                factor = (float)(((double)b.Factor - a.Factor) / ((double)b.Level - a.Level) * level + a.Factor);
                break;
            }
            if (multiplayerPlayers > 1)
                factor = (float)((1d + Math.Min(multiplayerPlayers - 1d, 6d) / 3d) * factor);
            health = Truncate((double)factor * balance.HealthDifficulties[SacredCreatureProgressionRules.DifficultyIndex(difficulty)] * health);
        }
        return health;
    }

    private static int Truncate(double value) => unchecked((int)(long)value);
}
