using Sacred.Core.GameBin.Balance;
using Sacred.Core.Runtime;

namespace Sacred.Core.Loot;

/// <summary>Source-specific rarity stages. These do not include drop chances,
/// category/resource selection or any world-object construction.</summary>
public static class SacredLootRarity
{
    public static SacredLootRarityResult Creature(IReadOnlyDictionary<uint, uint> weights,
        ushort sourceLevel, ushort heroLevel, uint difficulty, ushort findBonus,
        SacredLootBalance balance, SacredRandomSequence random)
    {
        if (heroLevel == 0) throw new ArgumentOutOfRangeException(nameof(heroLevel));
        var level = SacredCreatureProgressionRules.BaseLevelForDifficulty(sourceLevel, difficulty, balance.CreatureLevels);
        var ratio = (level + 9d) / (heroLevel + 9d);
        var factor = ratio >= balance.LevelRatioUpper ? 1f
            : (float)SacredLootLevelFactor.Calculate(sourceLevel, heroLevel, difficulty, balance);
        var scale = (float)(((1d - balance.RarityWeightFloor) * factor + balance.RarityWeightFloor) * 10d);
        var find = (float)(Math.Min(findBonus, (ushort)700) * (double).01f + 1d);
        return Draw(weights, scale, find, random);
    }

    public static SacredLootRarityResult Container(IReadOnlyDictionary<uint, uint> weights,
        ushort effectiveFind, uint survivalSeconds, SacredLootBalance balance, SacredRandomSequence random)
    {
        // Gold 0x5D5ACB: the stat getter includes a base 100. Preserve native
        // integer-indefinite for a negative square-root argument.
        var root = Truncate(Math.Sqrt((effectiveFind - 100d) * 100d));
        var score = Truncate(SurvivalCurve(survivalSeconds / 60, 0, 120, 50) * balance.SurvivalRarityFactor + root);
        var scale = (float)(((1d - balance.RarityWeightFloor) * score + balance.RarityWeightFloor) * 10d);
        var result = Draw(weights, scale, 1f, random);
        return result with { Quality = Math.Min(result.Quality, 11U) };
    }

    public static double SurvivalCurve(float input, float start, float divisor, float limit)
    {
        if (input < 1) return 0;
        var term = (limit - (double)start) * (1d - 1d / ((input - 1d) / divisor + 1d));
        return term + term + start;
    }

    private static SacredLootRarityResult Draw(IReadOnlyDictionary<uint, uint> weights,
        float scale, float find, SacredRandomSequence random)
    {
        var draw = random.Next() % 10000 + 1;
        uint cumulative = 0;
        foreach (var row in weights.OrderBy(row => row.Key))
        {
            // The multiplication order differs between these callers. Creature
            // multiplies find first; container find=1. Each row rounds before
            // the next row, with the accumulated DWORD interpreted as signed.
            var increment = (ushort)row.Value * (double)(row.Key == 16 ? 1f : find) * scale;
            cumulative = unchecked((uint)Truncate(increment + .5d + unchecked((int)cumulative)));
            if (cumulative > draw) return new(row.Key, row.Key is > 1 and < 16 ? 16 - row.Key : 0);
        }
        return new(17, 0);
    }

    private static int Truncate(double value) => double.IsFinite(value) && value < 9223372036854775808d && value >= -9223372036854775808d
        ? unchecked((int)(long)value) : 0;
}
