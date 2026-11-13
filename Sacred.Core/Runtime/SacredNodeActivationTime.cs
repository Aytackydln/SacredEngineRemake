namespace Sacred.Core.Runtime;

/// <summary>Gold 0x64071C..0x64075A in startup PC=24. Loads retain their
/// precision; multiply and add round separately before the double store.</summary>
public static class SacredNodeActivationTime
{
    public static double Next(double clock, uint milliseconds)
    {
        var arithmetic = new SacredResultArithmetic(SacredX87Precision.Single);
        return arithmetic.Add(arithmetic.Multiply(milliseconds, 0.001d), clock);
    }
}
