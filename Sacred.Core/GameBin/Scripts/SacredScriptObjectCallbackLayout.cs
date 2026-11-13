using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>CreateObj callback operand: one tag, then a Latin-1 NUL-terminated suffix.
/// Gold 0x485C10 collects use/take names; callback binding runs separately from creation.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = PrefixSize)]
public readonly struct SacredScriptObjectCallbackLayout
{
    public const int PrefixSize = 1;
    public const string TakePrefix = "Take:";
    public const string UsePrefix = "Use:";
    /// <summary>CreateNPC tag 0x60 prefixes this before its ordered callback append.
    /// Native reuses death-callback scratch and preserves trailing bytes after a shorter name.</summary>
    public const string SeePrefix = "See:";
    [FieldOffset(0)] public readonly SacredScriptArgumentKind Kind;
}
