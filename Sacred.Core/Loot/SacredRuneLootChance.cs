using Sacred.Core.GameBin.Balance;

namespace Sacred.Core.Loot;

/// <summary>Gold 0x5598B0, with resolved combat-art ranks and inventory rune
/// count. The caller compares this threshold with rand()%1000 * rollScale.</summary>
public static class SacredRuneLootChance
{
    public static double Calculate(ushort sourceLevel, ushort heroLevel, uint heroClass,
        uint learnedRankSum, uint inventoryRuneCount, bool boostedSource,
        SacredLootBalance balance, uint difficulty, float rollScale = 30)
    {
        var tuning = balance.Runes ?? throw new InvalidOperationException("Original rune loot tuning unavailable.");
        var index = heroClass switch { 1 => 1, 2 => 0, 3 => 4, 4 => 3, 5 => 2, 6 or 7 => 5, 8 => 7, 9 => 6, _ => -1 };
        var factor = index < 0 ? 1f : tuning.ClassLevelFactors[index];
        // Gold stores the combined denominator at single precision.
        var denominator = (float)(inventoryRuneCount / (double)tuning.InventoryDivisor + unchecked((int)learnedRankSum));
        var ratio = factor * (double)Math.Max(heroLevel, (ushort)1) / denominator;
        var power = ratio * ratio * ratio * ratio;
        var value = ratio > 1 || boostedSource ? power * ratio
            : SacredLootLevelFactor.Calculate(sourceLevel, heroLevel, difficulty, balance) * (float)power;
        value = Math.Min(Math.Max(value, (double).01f), tuning.RatioLimit);
        if (boostedSource) value *= tuning.BoostedFactor;
        return value * tuning.ChanceFactor * rollScale;
    }
}
