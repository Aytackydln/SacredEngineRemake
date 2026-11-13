using System.Numerics;

namespace Sacred.Core.Runtime;

/// <summary>Arithmetic precision for the bounded actor-result operations.
/// PC=24 rounds every arithmetic instruction, retaining the wider exponent.
/// Residuals preserve the direction at a 24-bit midpoint instead of introducing
/// a double rounding through the CLR's 53-bit intermediate. Loads stay exact.</summary>
internal readonly struct SacredResultArithmetic
{
    private readonly bool _single;

    public SacredResultArithmetic(SacredX87Precision precision) => _single = precision switch
    {
        SacredX87Precision.Single => true,
        SacredX87Precision.Double or SacredX87Precision.Extended => false,
        _ => throw new ArgumentOutOfRangeException(nameof(precision))
    };

    public double Add(double left, double right)
    {
        var result = left + right;
        if (!_single) return result;
        var virtualRight = result - left;
        var residual = (left - (result - virtualRight)) + (right - virtualRight);
        return Round(result, residual);
    }

    public double Subtract(double left, double right) => Add(left, -right);

    public double Multiply(double left, double right)
    {
        var result = left * right;
        return _single ? Round(result, Math.FusedMultiplyAdd(left, right, -result)) : result;
    }

    public double Divide(double left, double right)
    {
        var result = left / right;
        return _single ? Round(result, Math.FusedMultiplyAdd(-result, right, left) / right) : result;
    }

    private static double Round(double value, double residual)
    {
        if (!double.IsFinite(value) || value == 0) return value;
        var bits = BitConverter.DoubleToUInt64Bits(value);
        var magnitude = bits & 0x7FFFFFFFFFFFFFFF;
        var shift = (magnitude >> 52) != 0 ? 29 : Math.Max(0, BitOperations.Log2(magnitude) - 23);
        if (shift == 0) return value;
        var mask = (1UL << shift) - 1;
        var tail = magnitude & mask;
        var half = 1UL << (shift - 1);
        var up = tail > half || tail == half && ((bits >> 63) != 0 ? -residual : residual) > 0;
        if (tail == half && residual == 0) up = ((magnitude >> shift) & 1) != 0;
        magnitude &= ~mask;
        if (up) magnitude += 1UL << shift;
        return BitConverter.UInt64BitsToDouble((bits & 0x8000000000000000) | magnitude);
    }
}
