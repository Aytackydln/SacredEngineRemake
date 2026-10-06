using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>One headerless merc.bin sIconRegion record, consumed by the original map UI.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredRegionNpcIconLayout
{
    public const int SerializedSize = 16;
    [FieldOffset(0x00)] public readonly int RegionId;
    [FieldOffset(0x04)] public readonly int TileX;
    [FieldOffset(0x08)] public readonly int TileY;
    [FieldOffset(0x0C)] public readonly SacredRegionNpcKind Kind;
}

public enum SacredRegionNpcKind : int
{
    Merchant = 0,
    Blacksmith = 1,
    ComboMaster = 2,
    HorseTrader = 3
}

/// <summary>Literal prefix of Gold SetMapIcon (opcode 97). Three tagged integers
/// describe X/Y/icon type. The label form has four integers (X/Y/curvature/angle)
/// followed by tag 0x01 and a NUL-terminated resource reference. Gold narrows
/// both text-transform values to signed bytes before rendering.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredMapIconArgumentsLayout
{
    public const int SerializedSize = 15;
    [FieldOffset(0x00)] public readonly SacredScriptArgumentKind XKind;
    [FieldOffset(0x01)] public readonly int TileX;
    [FieldOffset(0x05)] public readonly SacredScriptArgumentKind YKind;
    [FieldOffset(0x06)] public readonly int TileY;
    [FieldOffset(0x0A)] public readonly SacredScriptArgumentKind ThirdKind;
    [FieldOffset(0x0B)] public readonly int IconTypeOrLabelCurvature;
}

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredMapLabelArgumentsLayout
{
    public const int SerializedSize = 21;
    [FieldOffset(0x00)] public readonly SacredMapIconArgumentsLayout Position;
    [FieldOffset(0x0F)] public readonly SacredScriptArgumentKind AngleKind;
    [FieldOffset(0x10)] public readonly int LabelAngleDegrees;
    [FieldOffset(0x14)] public readonly SacredScriptArgumentKind ResourceKind;
}
