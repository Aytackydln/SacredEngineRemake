namespace Sacred.Core.Executable.Layouts;

/// <summary>Gold 0x70B8AC..0x70BCFB lobby registration operand purposes. Offsets
/// are relative to the directory/registry block, not hardcoded registry values.</summary>
public static class SacredExecutableLobbyRegistrationLayout
{
    public const int BodySize = 0x44f;
    /// <summary>Structural prefix excludes the directory-capacity operand.</summary>
    public static ReadOnlySpan<byte> Signature => [0x8d, 0x4c, 0x24, 0x34, 0x88, 0x5c, 0x24, 0x34, 0x51, 0x68];
    public const int DirectoryCapacityOperand = 0xa;
    /// <summary>Signed imm8 in PUSH; the native argument is sign-extended UInt32.</summary>
    public const int CreateAccessOperand = 0x1c;
    public const int KeyPathOperand = 0x21;
    public const int RootKeyOperand = 0x26;
    public static ReadOnlySpan<int> ValueNameOperands => [0x117, 0x157, 0x18b, 0x1cb, 0x230, 0x2a1, 0x30e, 0x3bf];
    /// <summary>Signed imm8 PUSH operand preceding each value-name pointer.</summary>
    public const int ValueTypeOperandDistance = -3;
    public const int ApplicationNameOperand = 0xd9;
    public const int EmptyCommandOperand = 0x11f;
    public const int DescriptionOperand = 0x193;
    public const int LauncherPrefixOperand = 0x238;
    public const int LauncherSuffixOperand = 0x25c;
    public const int GuidOperand = 0x316;
}
