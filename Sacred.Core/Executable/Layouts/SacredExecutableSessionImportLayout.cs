namespace Sacred.Core.Executable.Layouts;

/// <summary>Gold 0x7EDEA0 importer operands, also inspected in installed pureHD.
/// Positions describe instructions, not copied key/mask literal values.</summary>
public static class SacredExecutableSessionImportLayout
{
    public static ReadOnlySpan<byte> Signature => [0x83, 0xec, 0x54, 0x53, 0x55, 0x56, 0x57, 0x8b, 0xf1];
    public const int SignatureOffset = 0x15;
    public const int BodySize = 0x8d5;
    public const int SessionDataKeyOperand = 0x28;
    public const int CharacterKeyOperand = 0x8b;
    public const int MaskedSuffixOperand = 0x196;
    public const int SecondMaskedSuffixOperand = 0x295;
    // pureHD has different CD-key handling before the shared importer fields.
    // These positions establish its data references, not equivalent control flow.
    public const int PureHdBodySize = 0x83b;
    public const int PureHdMaskedSuffixOperand = 0x246;
    public const int PureHdSecondMaskedSuffixOperand = 0x332;
}
