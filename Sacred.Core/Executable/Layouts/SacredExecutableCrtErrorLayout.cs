namespace Sacred.Core.Executable.Layouts;

/// <summary>Original _dosmaperr instruction operands and eight-byte table rows.</summary>
public static class SacredExecutableCrtErrorLayout
{
    public static ReadOnlySpan<byte> Signature => [0x8b, 0x4c, 0x24, 8, 0x33, 0xf6, 0x89, 8, 0xb8];
    public const int SignatureOffset = 6;
    public const int TableStartOperand = 0x0f;
    public const int TableEndOperand = 0x1c;
    public const int TableValueBaseOperand = 0x41;
    public const int RowSize = 8;
    public const int AccessMinimumOperand = 0x24;
    public const int AccessMaximumOperand = 0x29;
    public const int AccessErrorOperand = 0x33;
    public const int ExecutableMinimumOperand = 0x4b;
    public const int ExecutableMaximumOperand = 0x53;
    public const int ExecutableErrorOperand = 0x60;
    public const int DefaultErrorOperand = 0x6d;
}
