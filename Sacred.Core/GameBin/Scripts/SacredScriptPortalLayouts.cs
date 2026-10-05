using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>Tagged Int32 literal used by OnMoveOver, SetMapIcon and DefPos.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptIntegerArgumentLayout
{
    public const int SerializedSize = 5;
    [FieldOffset(0)] public readonly SacredScriptArgumentKind Kind; // 0x0B
    [FieldOffset(1)] public readonly int Value;
}

/// <summary>SetBaseTrigger operands 0x0C and 0x0D. Both contain X/Y/surface;
/// X == -2 replaces the remaining coordinates with a NUL-terminated DefPos name.
/// Gold 0x4819A0 normalizes the corners and makes a singleton when only one is present.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredScriptTriggerCornerLayout
{
    public const int SerializedSize = 13;
    [FieldOffset(0)] public readonly SacredScriptArgumentKind Kind;
    [FieldOffset(1)] public readonly int X;
    [FieldOffset(5)] public readonly int Y;
    /// <summary>Trigger surface level; Gold replaces values outside 0..16 with zero.</summary>
    [FieldOffset(9)] public readonly int SurfaceLevel;
}

public static class SacredScriptPortalOpcodes
{
    public const ushort SetBaseTrigger = 4;
    public const ushort DeleteBaseTrigger = 5;
    public const ushort DefinePosition = 23;
    public const ushort OnMoveOver = 39;
    public const ushort Teleport = 46;
    public const ushort If = 58;
    public const ushort EndBlock = 62; // Native NOP terminates compiled IF/ELSE blocks.
    public const ushort SetMapIcon = 97;
    public const int PortalMapIcon = 1;
}
