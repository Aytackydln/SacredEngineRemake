using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Fixed prefix of a named tile position. Followed at NameOffset by a variable-length
/// NUL-terminated Latin-1 DefPos name instead of literal Y/surface coordinates.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = PrefixSize)]
public readonly struct SacredScriptSymbolicPositionArgumentLayout
{
    public const int PrefixSize = 5;
    public const int NameOffset = PrefixSize;
    public const int SymbolicSentinel = -2;
    [FieldOffset(0)] public readonly SacredScriptArgumentKind Kind;
    [FieldOffset(1)] public readonly int Sentinel;
}
