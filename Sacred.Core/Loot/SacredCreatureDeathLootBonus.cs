using Sacred.Core.GameBin.Balance;

namespace Sacred.Core.Loot;

/// <summary>Gold 0x534595..0x534646: death caller's rarity argument, before
/// 0x559BF0 repeatedly rescales it using the dying creature's experience field.</summary>
public static class SacredCreatureDeathLootBonus
{
    public static uint Calculate(uint sourceBonus, uint killerReference, ushort effectiveFind,
        uint survivalSeconds, SacredLootBalance balance)
    {
        if (killerReference > 16) return sourceBonus;
        var survival = SacredLootRarity.SurvivalCurve(survivalSeconds / 60, 0, 120, 50);
        var bonus = Truncate(survival * balance.SurvivalRarityFactor + unchecked((int)sourceBonus));
        // Native rounds after adding the square root, not before. Negative
        // arguments produce the x87 integer-indefinite whose low DWORD is zero.
        return Truncate(Math.Sqrt((effectiveFind - 100d) * 100d) + bonus);
    }

    private static uint Truncate(double value) => double.IsFinite(value) && value >= long.MinValue && value < 9223372036854775808d
        ? unchecked((uint)(long)value) : 0;
}
