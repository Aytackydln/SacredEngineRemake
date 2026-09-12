using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Five-byte height operand. At 0x473A86 Sacred.exe SIGN EXTENDS a 16-bit
/// value but advances over four payload bytes. This is not an Int32 height.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptHeightArgumentLayout
{
    public const int SerializedSize = 5;
    [FieldOffset(0)] public readonly SacredScriptArgumentKind Kind;
    [FieldOffset(1)] public readonly short HeightOffset;
    [FieldOffset(3), BinaryUnknown] public readonly ushort Unknown03;
}
