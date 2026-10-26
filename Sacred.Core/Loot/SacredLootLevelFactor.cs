using Sacred.Core.GameBin.Balance;
using Sacred.Core.Runtime;

namespace Sacred.Core.Loot;

/// <summary>Gold 0x551530, including difficulty reversal, float storage boundaries,
/// low/high hero-level thresholds and the native four-percent floor.</summary>
public static class SacredLootLevelFactor
{
    public static double Calculate(ushort sourceLevel, ushort heroLevel, uint difficulty, SacredLootBalance balance)
    {
        var level = SacredCreatureProgressionRules.BaseLevelForDifficulty(sourceLevel, difficulty, balance.CreatureLevels);
        var numerator = (float)(level + 9);
        var denominator = (float)(heroLevel + 9);
        var ratio = (float)(numerator / (double)denominator);
        var lower = heroLevel < 11 ? (heroLevel + 6d) / denominator
            : heroLevel > 91 ? (heroLevel - 6d) / denominator : balance.LevelRatioLower;
        if (ratio <= lower) return .04f;
        var factor = ratio > balance.LevelRatioUpper ? denominator / (double)numerator * balance.LevelRatioUpper
            : ratio >= balance.LevelRatioPlateau ? 1d : (ratio - lower) / (balance.LevelRatioPlateau - lower);
        return Math.Max(.04f, factor);
    }
}
