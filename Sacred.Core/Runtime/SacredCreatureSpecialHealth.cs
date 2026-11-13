namespace Sacred.Core.Runtime;

/// <summary>Gold 0x565AFA..0x565B4B arithmetic after selecting the original
/// Balance factor and executable multiplier. PC=24 startup is compared; wider
/// modes need their own intermediate/conversion comparisons.</summary>
public static class SacredCreatureSpecialHealth
{
    public static float FactorStartup(float factor, double multiplier)
        => (float)new SacredResultArithmetic(SacredX87Precision.Single).Multiply(multiplier, factor);

    public static int ApplyStartup(int health, float factor, double multiplier)
    {
        var arithmetic = new SacredResultArithmetic(SacredX87Precision.Single);
        var selected = arithmetic.Multiply(multiplier, factor);
        return unchecked((int)(long)arithmetic.Multiply(health, selected));
    }
}
