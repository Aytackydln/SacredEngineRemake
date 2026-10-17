using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold 0x472BC0 integer operand: -1234567 replaces the literal with a
/// following NUL-terminated variable name. Other signed values remain literals.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = PrefixSize)]
public readonly struct SacredScriptVariableArgumentLayout
{
    public const int PrefixSize = 5;
    public const int VariableSentinel = -1234567;
    [FieldOffset(0)] public readonly SacredScriptArgumentKind Kind;
    [FieldOffset(1)] public readonly int Value;
}
