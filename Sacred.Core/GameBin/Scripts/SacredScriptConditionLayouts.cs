using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Tag 0x6D at Gold 0x473C48: Int32 comparison followed by a NUL-terminated variable name.
/// Nonnegative values are literal thresholds; negative values can select additional resolver operands.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptVariableComparisonLayout
{
    public const int SerializedSize = 5;
    public const byte GreaterThanTag = 0x6D;
    [FieldOffset(0)] public readonly byte Tag;
    [FieldOffset(1)] public readonly int Threshold;
}

public static class SacredScriptConditionTags
{
    /// <summary>Gold 0x498C2A: true only when the native multiplayer flag is zero.</summary>
    public const byte MultiplayerInactive = 0xA0;
}
