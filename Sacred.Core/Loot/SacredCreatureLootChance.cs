using Sacred.Core.GameBin.Balance;

namespace Sacred.Core.Loot;

/// <summary>Gold 0x559DA0..0x559F17: initial creature drop chance budget.
/// Caller supplies resolved eligible party count, source-boost flag and the
/// original curve argument (death caller: skill 42/table 55's resolved word,
/// not save survival seconds). This does not determine item selection retries.</summary>
public static class SacredCreatureLootChance
{
    public static int Calculate(SacredLootBalance balance, uint nearbyPartyMembers, bool boostedSource, float survival)
    {
        if (!float.IsFinite(survival)) throw new ArgumentOutOfRangeException(nameof(survival));
        var chance = (int)balance.CreatureBaseChance;
        chance = (int)((1d + Math.Min(nearbyPartyMembers, 4U) * (double)balance.CreaturePartyMemberChanceFactor) * chance);
        if (boostedSource) chance = (int)(chance * (double)balance.CreatureBoostedChanceFactor);
        var factor = SacredLootRarity.SurvivalCurve(survival, 10, 100, 100);
        return (int)((factor + 100d) * chance * .01f);
    }
}
