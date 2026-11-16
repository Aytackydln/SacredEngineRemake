using Sacred.Core.GameBin.Balance;

namespace Sacred.Core.Runtime;

/// <summary>Gold 0x484622..0x484714: the elapsed counter's seconds/minutes,
/// native 0x565D70 curve, truncation, then the progress-byte addition.</summary>
public static class SacredCreatureAutomaticLevelMath
{
    public static uint ExtraPercent(uint elapsedMilliseconds, byte progress, uint difficulty,
        SacredCreatureAutomaticLevelTuning tuning, SacredX87Precision precision)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        if (tuning.ProgressAdditions.Count != SacredCreatureBalanceLayout.DifficultyCount ||
            !float.IsFinite(tuning.TimeDivisor) || tuning.TimeDivisor <= 0)
            throw new InvalidDataException("Incomplete original automatic-level tuning.");
        var fp = new SacredResultArithmetic(precision);
        var minutes = elapsedMilliseconds / 1000 / 60;
        double curve = 0;
        if (minutes >= 1)
        {
            var offset = fp.Divide(minutes - 1, tuning.TimeDivisor);
            var fraction = fp.Subtract(1, fp.Divide(1, fp.Add(offset, 1)));
            curve = fp.Multiply(fraction, tuning.TimeLimit);
            curve = fp.Add(curve, curve);
        }
        var extra = unchecked((uint)(long)fp.Multiply(curve, tuning.TimeMultiplier));
        if (progress >= tuning.ProgressThreshold)
            extra = unchecked((uint)(long)fp.Add(extra,
                tuning.ProgressAdditions[SacredCreatureProgressionRules.DifficultyIndex(difficulty)]));
        return extra;
    }
}
