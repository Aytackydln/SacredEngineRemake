using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold 0x473FBC: literal pair operands 0x87/0x88/0x89 consume nine bytes.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptIntegerPairArgumentLayout
{
    public const int SerializedSize = 9;
    [FieldOffset(0)] public readonly SacredScriptArgumentKind Kind;
    [FieldOffset(1)] public readonly int First;
    [FieldOffset(5)] public readonly int Second;
}
