using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Literal position operand (0x04 or 0x20). Tag 0x04 with X == -2 uses a
/// different, symbolic encoding and MUST NOT be read using this layout.
/// Native decoders: 0x474065, 0x474181, 0x474703.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptPositionArgumentLayout
{
    public const int SerializedSize = 13;
    [FieldOffset(0)] public readonly SacredScriptArgumentKind Kind;
    [FieldOffset(1)] public readonly int X;
    [FieldOffset(5)] public readonly int Y;

    /// <summary>Third coordinate passed separately from the 0x7E height offset.
    /// Retained as Z until its surface/elevation semantics are fully recovered.</summary>
    [FieldOffset(9)] public readonly int Z;
}
