using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Tags 0x48/0x6D/0x6E in Gold ordinary-variable predicates: Int32 comparison followed by a NUL-terminated variable name.
/// Nonnegative values are literal thresholds; negative values can select additional resolver operands.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptVariableComparisonLayout
{
    public const int SerializedSize = 5;
    /// <summary>Equality, used by the random-pool startup branches (Gold 0x4987B0).</summary>
    public const byte EqualTag = 0x48;
    public const byte GreaterThanTag = 0x6D;
    public const byte LessThanTag = 0x6E;
    [FieldOffset(0)] public readonly byte Tag;
    [FieldOffset(1)] public readonly int Threshold;
}

public static class SacredScriptConditionTags
{
    /// <summary>Gold 0x498DA8: NUL-terminated literal hero type name. A missing
    /// active hero fails this condition, independently of the resolved type.</summary>
    public const byte IsHero = 0x52;
    /// <summary>Gold 0x498C18: true when the native multiplayer byte is nonzero.</summary>
    public const byte MultiplayerActive = 0x91;
    /// <summary>Gold 0x498C2A: true only when the native multiplayer flag is zero.</summary>
    public const byte MultiplayerInactive = 0xA0;
}
