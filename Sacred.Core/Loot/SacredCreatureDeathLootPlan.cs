using Sacred.Core.GameBin.Balance;
using Sacred.Core.Runtime;

namespace Sacred.Core.Loot;

/// <summary>Gold 0x533D09..0x533E11: source count/bonus before the resource
/// dispatch for boosted sources. Source-specific fixed rewards still follow.</summary>
public readonly record struct SacredCreatureDeathLootPlan(uint Count, uint Bonus, bool RequiresBoostedSourceDispatch)
{
    public static SacredCreatureDeathLootPlan Create(ushort sourceLevel, ushort heroLevel, uint difficulty,
        bool markedSource, bool boostedSource, SacredLootBalance balance, SacredRandomSequence random)
    {
        var baseLevel = SacredCreatureProgressionRules.BaseLevelForDifficulty(sourceLevel, difficulty, balance.CreatureLevels);
        var ratio = (baseLevel + 9d) / (heroLevel + 9d);
        var eligible = ratio >= balance.LevelRatioUpper ||
            SacredLootLevelFactor.Calculate(sourceLevel, heroLevel, difficulty, balance) >= .04d + 1.1920928955078125e-7;
        var count = balance.CreatureDeathBaseRollCount;
        uint bonus = 0;
        if (markedSource)
        {
            if (eligible) count = unchecked(count + (uint)SacredRandomRange.Inclusive(1, balance.CreatureMarkedExtraRollMaximum, random));
            bonus = balance.CreatureMarkedRarityBonus;
        }
        if (boostedSource)
        {
            if (eligible) count = unchecked(count + (uint)SacredRandomRange.Inclusive(1, balance.CreatureBoostedExtraRollMaximum, random));
            bonus = unchecked(bonus + balance.CreatureBoostedRarityBonus);
        }
        return new(count, bonus, boostedSource);
    }
}
