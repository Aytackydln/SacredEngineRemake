namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold SetIcon: optional NUL-terminated dialog name (tag 1), then integer
/// selector (tag 0x0B). No name selects the interpreter's current dialog.</summary>
public static class SacredScriptDialogIconLayout
{
    public const ushort Opcode = 86;
}

public readonly record struct SacredScriptDialogIconChange(string? DialogName, int Icon);
