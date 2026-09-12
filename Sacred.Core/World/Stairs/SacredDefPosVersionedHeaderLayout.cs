using System.Runtime.InteropServices;

namespace Sacred.Core.World.Stairs;

/// <summary>
/// Gold's compiled DefPos.bin header. Other installed tables begin directly with the count.
/// Gold 0x46FC5C tests the marker and 0x46FE71 reads the following count.
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredDefPosVersionedHeaderLayout
{
    public const int SerializedSize = 8;
    public const uint FormatMarker = 1234;

    [FieldOffset(0x00)] public readonly uint Marker;
    [FieldOffset(0x04)] public readonly uint PositionCount;
}
