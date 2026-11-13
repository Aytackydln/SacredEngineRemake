namespace Sacred.Core.Runtime;

/// <summary>Gold 0x57F6E0 in startup PC=24. The four positive polynomial
/// terms retain the native order and each arithmetic instruction rounds before
/// truncation; the final unsigned word is rounded down to hundreds. Arguments
/// 1..100 only: larger inputs require the original CRT power rounding.</summary>
public static class SacredStartupExperience
{
    public static uint AtLevel(ushort level)
    {
        if (level == 0) throw new ArgumentOutOfRangeException(nameof(level));
        if (level > 100)
            throw new NotSupportedException("Higher startup XP requires the original CRT power rounding; compared arguments are 1..100.");
        var fp = new SacredResultArithmetic(SacredX87Precision.Single);
        var n = (ulong)level;
        var value = fp.Add((float)(n * n * n * n), 110);
        value = fp.Add(value, fp.Multiply((float)(n * n * n), 40));
        value = fp.Add(value, fp.Multiply((float)(n * n), 150));
        value = fp.Add(value, unchecked((uint)(200 * level - 200)));
        if (value >= 9223372036854775808d) return 0;
        return unchecked((uint)(long)value) / 100 * 100;
    }
}
