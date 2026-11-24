namespace Sacred.Core.Executable.Layouts;

/// <summary>Original 0x662710 loader / 0x662DF0 integer writer and their
/// inspected installed counterparts. Data values are read from operands.</summary>
public static class SacredExecutableSettingsFileLayout
{
    public static ReadOnlySpan<byte> LoaderSignature => [0x81, 0xec, 0x88, 1, 0, 0, 0x55, 0x8b, 0xe9, 0x56, 0x57, 0x8d, 0x4c, 0x24, 0x24];
    public const int LoaderSignatureOffset = 0x15;
    public const int OpenModeOperand = 0x5f;
    public const int FirstCapacityOperand = 0x83;
    public const int NextCapacityOperand = 0x34b;
    public static ReadOnlySpan<int> SkipPrefixOperands => [0x121, 0x145, 0x169, 0x187];
    public const int ValueDelimitersOperand = 0x1a6;
    public const int CallerFileScanOperand = -0x49;
    public const int CallerDelimiterScanOperand = -0x92;
    public const int CallerEndBaseDistance = 0x10;
    public const int CallerRangeStartDistance = 0x16;
    public static ReadOnlySpan<byte> IntegerWriterSignature => [0x8b, 0x44, 0x24, 0x68, 0x56, 0x8b, 0xf1, 0x57, 0x50, 0x8d, 0x4c, 0x24, 0x40, 0x68];
    public const int IntegerWriterSignatureOffset = 0x18;
    public const int IntegerFormatOperand = 0x26;
}
