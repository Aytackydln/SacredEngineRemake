namespace Sacred.Core.GameBin.Scripts;

/// <summary>Gold 0x453970 checks this prefix inside numeric operands (including CreateNPC level).
/// Five prefix bytes are followed by a Latin-1 NUL-terminated variable name. This is distinct
/// from the tagged integer's -1234567 sentinel.</summary>
public static class SacredScriptVariableExpressionLayout
{
    public const byte Marker = 0x9F;
    public const uint Signature = 0xFEEDBEEF;
    public const int PrefixSize = 5;
}
