using System.Runtime.InteropServices;
using Sacred.Core.Runtime;
namespace Sacred.Core.GameBin.Balance;

/// <summary>Balance.bin globals +0x830: nine 12-byte HorseClass value curves.
/// Gold 0x461B52 uses all coefficients; SetOwner 0x4626A2 loads Base three times.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredHorseValueCurveLayout
{
    public const int Size = 12;
    [FieldOffset(0)] public readonly float Base;
    [FieldOffset(4)] public readonly float Linear;
    [FieldOffset(8)] public readonly float Quadratic;
}
public sealed class SacredHorseValueBalance(IReadOnlyList<SacredHorseValueCurveLayout> curves)
{
    public const int GlobalOffset = 0x830;
    public const int Count = 9;
    private readonly SacredHorseValueCurveLayout[] _curves = curves.ToArray();
    public uint OwnerValue(ushort level, byte horseClass, SacredX87Precision precision)
    {
        if (level is < 1 or > 101 || _curves.Length != Count) throw new NotSupportedException("Horse owner value requires compared startup levels and all nine original curves.");
        var fp = new SacredResultArithmetic(precision);
        var coefficient = _curves[Math.Clamp((int)horseClass,1,Count)-1].Base;
        var n = level - 1;
        var value = fp.Add(fp.Multiply(fp.Add(fp.Multiply(coefficient,n),coefficient),n),coefficient);
        return unchecked((uint)(int)value);
    }
}
