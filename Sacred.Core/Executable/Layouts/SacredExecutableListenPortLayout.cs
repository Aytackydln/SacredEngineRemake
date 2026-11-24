namespace Sacred.Core.Executable.Layouts;

/// <summary>Original listen-port call at controller+0x40 write site and getter/CRT
/// operand purposes. Identification excludes key/default/classification data.</summary>
public static class SacredExecutableListenPortLayout
{
    public static ReadOnlySpan<byte> CallerPrefix => [0x88, 0x5c, 0x24, 0x6c, 0xe8];
    public static ReadOnlySpan<byte> CallerSuffix => [0x89, 0x46, 0x40, 0x50];
    public const int CallerSuffixOffset = 9;
    public const int GetterCallOffset = 4;
    public static ReadOnlySpan<byte> DestinationWrite => [0x89, 0x46, 0x40];
    public const int GoldCallDistance = 5;
    /// <summary>pureHD pushes the value/format argument before the same write.</summary>
    public const int PureHdCallDistance = 11;
    public const int GetterBodySize = 0x1ad;
    public const int ReadKeyOperand = 0x30;
    public const int DefaultKeyOperand = 0xff;
    // mov edi supplies strlen input; add ecx and push supply independent
    // range end-base/start operands for the actual byte key construction.
    public const int KeyEndBaseOperandDistance = 0x15;
    public const int KeyRangeStartOperandDistance = 0x1b;
    public const int DefaultWriteOperand = 0x14d;
    public const int DefaultReturnOperand = 0x19d;
    public const int NumericCallOffset = 0xb1;
    public const int NumericThunkCallOffset = 4;
    public const int ClassPointerOperand = 0x25;
    public const int ClassCount = 256;
    public const ushort WhitespaceMask = 8;
    public const ushort DigitMask = 4;
}
