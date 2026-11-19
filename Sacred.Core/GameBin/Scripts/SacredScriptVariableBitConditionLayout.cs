namespace Sacred.Core.GameBin.Scripts;

/// <summary>FunkCode IF/ElseIf ordinary-variable set-bit clause: tag, Int32
/// bit index, then Latin-1 terminated variable name. Consecutive clauses AND;
/// 0x48 remains the separate scalar equality predicate.</summary>
public static class SacredScriptVariableBitConditionLayout
{
    public const byte SetTag = 0x49;
    public const int IndexOffset = 1;
    public const int NameOffset = 5;
}
