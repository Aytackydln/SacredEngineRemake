using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold opcode 106 association row at interpreter +0x7568. Gaps retain
/// caller scratch bytes; this mapping does not provide an initialized row template.
/// The native 0x549A26 lookup reads only slot, type and qualifier. Bank 3 section 5
/// nevertheless transfers the full row, including 74 unwritten caller bytes.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptDialogAssociationLayout
{
    public const int SerializedSize = 88;
    public const int RegistrationSlotOffset = 0;
    public const int DefaultWordOffset = 72;
    public const int TypeReferenceOffset = 80;
    public const int QualifierOffset = 84;
    public const int ConstructorWrittenSize = 14;
    public const int UnwrittenSize = SerializedSize - ConstructorWrittenSize;
    public static bool IsConstructorWrittenByte(int offset)
    {
        if ((uint)offset >= SerializedSize) throw new ArgumentOutOfRangeException(nameof(offset));
        return offset < RegistrationSlotOffset + sizeof(int) ||
            offset >= DefaultWordOffset && offset < DefaultWordOffset + sizeof(uint) ||
            offset >= TypeReferenceOffset && offset < QualifierOffset + sizeof(ushort);
    }
    [FieldOffset(RegistrationSlotOffset)] public readonly int RegistrationSlot;
    /// <summary>New opcode-106 rows write 0x0000FFFF here; updates preserve the previous value.</summary>
    [FieldOffset(DefaultWordOffset)] public readonly uint DefaultWord72;
    [FieldOffset(TypeReferenceOffset)] public readonly uint TypeReference;
    [FieldOffset(QualifierOffset)] public readonly ushort Qualifier;
}
