using System.Numerics;

namespace Sacred.Core.Runtime;

/// <summary>Gold 0x565C4E..0x565C8A. The original double reciprocal of three
/// remains unrounded on the x87 stack through addition/multiplication. Preserve
/// its exact binary product, round to 64 significant bits, then store float.
/// A managed division by three would discard that reciprocal's residual.</summary>
internal static class SacredCreatureMultiplayerHealth
{
    public static float Scale(float factor, uint players, SacredX87Precision precision)
    {
        if (players <= 1 || factor == 0 || !float.IsFinite(factor)) return factor;
        if (precision == SacredX87Precision.Single)
        {
            var fp = new SacredResultArithmetic(precision);
            var playerFactor = fp.Add(fp.Multiply(Math.Min(players - 1, 6u), 1d / 3d), 1);
            return (float)fp.Multiply(playerFactor, factor);
        }
        if (precision == SacredX87Precision.Double)
            return (float)((1d + Math.Min(players - 1d, 6d) * (1d / 3d)) * factor);
        var bits = BitConverter.SingleToUInt32Bits(factor);
        var field = (int)((bits >> 23) & 255);
        UInt128 significand = bits & 0x7FFFFF;
        if (field != 0) significand |= 1u << 23;
        var exponent = (field == 0 ? -149 : field - 150) - 54;
        // Native 1/3 operand: 6004799503160661 * 2^-54. For counts 1..6,
        // both its product and the following +1 fit the x87 significand exactly.
        var multiplier = (1UL << 54) + Math.Min(players - 1, 6u) * 6004799503160661UL;
        significand *= multiplier;
        Round(ref significand, ref exponent, Math.Max(0, Length(significand) - 64));
        var top = exponent + Length(significand) - 1;
        if (top < -126)
        {
            Round(ref significand, ref exponent, Math.Max(0, -149 - exponent));
            return BitConverter.UInt32BitsToSingle((bits & 0x80000000) | (uint)significand);
        }
        Round(ref significand, ref exponent, Math.Max(0, Length(significand) - 24));
        if (Length(significand) > 24) { significand >>= 1; exponent++; }
        var biased = exponent + 150;
        var result = biased >= 255 ? 0x7F800000u : ((uint)biased << 23) | ((uint)significand & 0x7FFFFF);
        return BitConverter.UInt32BitsToSingle((bits & 0x80000000) | result);
    }

    private static int Length(UInt128 value) => value >> 64 != 0
        ? BitOperations.Log2((ulong)(value >> 64)) + 65 : BitOperations.Log2((ulong)value) + 1;

    private static void Round(ref UInt128 value, ref int exponent, int shift)
    {
        if (shift == 0) return;
        var tail = value & (((UInt128)1 << shift) - 1);
        var half = (UInt128)1 << (shift - 1);
        value >>= shift;
        if (tail > half || tail == half && (value & 1) != 0) value++;
        exponent += shift;
    }
}
