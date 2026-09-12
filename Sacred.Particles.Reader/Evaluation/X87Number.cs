using System.Numerics;

namespace Sacred.Particles.Reader.Evaluation;

/// <summary>Finite binary floating point with the x87 extended 64-bit significand.
/// Preset arithmetic needs its rounding at integer color boundaries; double's
/// 53-bit significand can change a truncated channel by one.</summary>
internal readonly record struct X87Number(BigInteger Significand, int Exponent)
{
    public static implicit operator X87Number(double value)
    {
        if (!double.IsFinite(value)) throw new NotSupportedException("Nonfinite native preset value.");
        var bits = BitConverter.DoubleToUInt64Bits(value);
        var exponent = (int)((bits >> 52) & 2047);
        var mantissa = new BigInteger(bits & 0xFFFFFFFFFFFFF);
        if (exponent != 0) mantissa += BigInteger.One << 52;
        if ((bits >> 63) != 0) mantissa = -mantissa;
        return new X87Number(mantissa, exponent == 0 ? -1074 : exponent - 1075);
    }

    public double ToDouble() => Math.ScaleB((double)Significand, Exponent);

    public BigInteger ToInteger(int rounding)
    {
        if (Exponent >= 0) return Significand << Exponent;
        var denominator = BigInteger.One << -Exponent;
        var whole = BigInteger.DivRem(Significand, denominator, out var remainder);
        if (remainder.IsZero) return whole;
        return rounding switch
        {
            1 => Significand.Sign < 0 ? whole - 1 : whole,
            2 => Significand.Sign > 0 ? whole + 1 : whole,
            0 when BigInteger.Abs(remainder) * 2 > denominator ||
                   (BigInteger.Abs(remainder) * 2 == denominator && !whole.IsEven) => whole + Significand.Sign,
            _ => whole
        };
    }

    public static X87Number operator +(X87Number a, X87Number b)
    {
        var exponent = Math.Min(a.Exponent, b.Exponent);
        return Round((a.Significand << (a.Exponent - exponent)) +
                     (b.Significand << (b.Exponent - exponent)), BigInteger.One, exponent);
    }

    public static X87Number operator -(X87Number a, X87Number b) => a + new X87Number(-b.Significand, b.Exponent);
    public static X87Number operator *(X87Number a, X87Number b) =>
        Round(a.Significand * b.Significand, BigInteger.One, a.Exponent + b.Exponent);
    public static X87Number operator /(X87Number a, X87Number b) =>
        Round(a.Significand, b.Significand, a.Exponent - b.Exponent);

    private static X87Number Round(BigInteger numerator, BigInteger denominator, int exponent)
    {
        if (denominator.IsZero) throw new NotSupportedException("Division by zero in native preset.");
        if (numerator.IsZero) return default;
        var sign = numerator.Sign * denominator.Sign;
        numerator = BigInteger.Abs(numerator);
        denominator = BigInteger.Abs(denominator);
        var shift = 63 - (int)(numerator.GetBitLength() - denominator.GetBitLength());
        if (shift >= 0) numerator <<= shift;
        else denominator <<= -shift;
        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        // Ensure 64 significant bits even when the estimated ratio exponent is one too high.
        if (quotient.GetBitLength() < 64)
        {
            numerator <<= 1;
            shift++;
            quotient = BigInteger.DivRem(numerator, denominator, out remainder);
        }
        if (remainder * 2 > denominator || (remainder * 2 == denominator && !quotient.IsEven)) quotient++;
        return new X87Number(sign * quotient, exponent - shift);
    }
}
