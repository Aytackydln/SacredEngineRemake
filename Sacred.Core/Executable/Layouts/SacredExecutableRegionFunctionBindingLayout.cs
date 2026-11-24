namespace Sacred.Core.Executable.Layouts;

/// <summary>Gold load-time Region/Sector name-binding operands.</summary>
public static class SacredExecutableRegionFunctionBindingLayout
{
    public static ReadOnlySpan<byte> Signature => [0xb9,0x20,0,0,0,0x33,0xc0,0x8d,0xbc,0x24,0x58,2,0,0,0xf3,0xab];
    public const int FirstPrefixOperand = 0x51;
    public const int SecondPrefixOperand = 0x62;
    public const int DigitCallOffset = 0x78;
    public const int ClassPointerOperand = 0x1d;
    public const int PrefixLength = 6;
    public const int ClassifiedCharacterOffset = 6;
    public const int SignedClassMinimum = -128;
    public const int SignedClassCount = 256;
}
