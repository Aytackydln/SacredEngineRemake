using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Runtime;

/// <summary>Gold 0x579FBF..0x57A153 armor movement factor. The caller keeps
/// the smallest factor in native equipment order. Modifier is the original
/// wpmod coefficient; missing and out-of-range coefficients leave factor one.</summary>
public static class SacredCreatureArmorLevelPenalty
{
    public static float Calculate(SacredX87Precision precision, ushort level, byte armorLoreRank,
        byte itemLevel, int? modifier)
    {
        if (itemLevel == 0 || itemLevel <= level + armorLoreRank || modifier is not (> 0 and < 1000)) return 1;
        var fp = new SacredResultArithmetic(precision);
        var ratio = fp.Divide(fp.Add(level + armorLoreRank, 9), fp.Add(itemLevel, 9));
        var power = Square(ratio, fp);
        var reduction = fp.Multiply(fp.Multiply(fp.Subtract(1, power), modifier.Value), 0.01d);
        return (float)fp.Subtract(1, reduction);
    }

    private static double Square(double ratio, SacredResultArithmetic fp)
    {
        // Original CRT 0x849FA0/0x850BD0 uses FYL2X, rounded integer/fraction
        // splitting, F2XM1, FADD 1 and FSCALE even for exponent two. The FADD
        // rounds at PC=24; replacing the body with ratio*ratio changes results.
        var exponent = Math.Log2(ratio) * 2;
        var integral = Math.Round(exponent, MidpointRounding.ToEven);
        var fractional = fp.Subtract(exponent, integral);
        var power = fp.Add(Math.Pow(2, fractional) - 1, 1);
        return Math.ScaleB(power, (int)integral);
    }
}
