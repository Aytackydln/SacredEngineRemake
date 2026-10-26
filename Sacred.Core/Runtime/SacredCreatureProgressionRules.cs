using Sacred.Core.GameBin.Balance;

namespace Sacred.Core.Runtime;

/// <summary>Gold 0x552230 / 0x57F6E0 arithmetic, independent of actor ownership and level caps.</summary>
public static class SacredCreatureProgressionRules
{
    public static int DifficultyIndex(uint difficulty) => difficulty switch { 1 => 0, 2 => 1, 3 => 2, 4 => 3, _ => 5 };

    /// <summary>Gold 5522D0 reverses difficulty level growth for the enemy health-ring colour.</summary>
    public static uint BaseLevelForDifficulty(uint level, uint difficulty, SacredCreatureBalance balance)
    {
        var index = DifficultyIndex(difficulty);
        var addition = unchecked((uint)balance.LevelAdditions[index]);
        if (level <= addition) return 1;
        var value = (level - addition) / (1d + balance.LevelPercentAdditions[index] * (double).01f) + .5d;
        // Native's lower-bound branch loads 1.1f before __ftol, producing level one.
        return value >= 1 && double.IsFinite(value) ? unchecked((uint)(long)value) : 1;
    }

    public static uint NormalizeLevel(uint level, uint difficulty, SacredCreatureBalance balance, uint extraPercent = 0)
    {
        var index = DifficultyIndex(difficulty);
        var percent = unchecked((uint)balance.LevelPercentAdditions[index] + extraPercent);
        // Native uses a float 0.01 promoted to x87 extended precision, and signed
        // FIMUL for level. The percent scratch is zero-extended before FILD.
        var result = (1d + percent * (double)0.01f) * unchecked((int)level) + balance.LevelAdditions[index] + 0.5d;
        return unchecked((uint)(long)Math.Max(1d, result));
    }

    public static uint ExperienceAtLevel(ushort level)
    {
        // Native's first pow is 1^level, hence its contribution is constant 110.
        // The remaining Gold coefficients at 0x957020 are -200,200,150,40,1.
        var x = (double)level;
        var value = x * x * x * x + 40 * x * x * x + 150 * x * x + 200 * x - 90;
        // __ftol returns a 64-bit integer; the writer retains EAX before /100*100.
        // On signed 64-bit overflow x87 supplies the integer-indefinite value,
        // whose low word is zero. Managed floating casts need not do the same.
        if (value >= 9223372036854775808d) return 0;
        return unchecked((uint)(long)value) / 100 * 100;
    }

    public static byte ActiveSkillCount(ushort level) => level < 3 ? (byte)0 : level < 7 ? (byte)1
        : level < 21 ? (byte)2 : level <= 35 ? (byte)3 : (byte)8;
}
