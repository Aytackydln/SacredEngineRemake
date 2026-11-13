namespace Sacred.Core.Runtime;

/// <summary>Gold MAGICPRISON Start arithmetic. Coefficients are executable
/// inputs; startup uses x87 PC=24 and truncates each packed color component.</summary>
public static class SacredMagicPrisonInitialization
{
    public static uint[] CreatePalette(int count, float indexScale, float greyScale, float blueScale)
    {
        if (count is < 1 or > 256 || !float.IsFinite(indexScale) || !float.IsFinite(greyScale) ||
            !float.IsFinite(blueScale)) throw new ArgumentOutOfRangeException(nameof(count));
        var arithmetic = new SacredResultArithmetic(SacredX87Precision.Single);
        var palette = new uint[count];
        for (var index = 0; index < count; index++)
        {
            var scaled = arithmetic.Multiply(index, indexScale);
            var grey = unchecked((uint)(int)arithmetic.Multiply(scaled, greyScale));
            var blue = unchecked((uint)(int)arithmetic.Multiply(scaled, blueScale));
            palette[index] = unchecked((((grey << 8) | grey) << 8) | blue);
        }
        return palette;
    }

    public static uint RateBits(byte detailLevel, float low, float medium, float high, float multiplier)
    {
        if (detailLevel > 2) throw new NotSupportedException("MAGICPRISON detail input has not been compared above two.");
        var source = detailLevel switch { 2 => high, 1 => medium, _ => low };
        return BitConverter.SingleToUInt32Bits((float)new SacredResultArithmetic(SacredX87Precision.Single).Multiply(source, multiplier));
    }
}
