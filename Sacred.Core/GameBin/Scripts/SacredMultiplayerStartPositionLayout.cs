using System.Runtime.InteropServices;

namespace Sacred.Core.GameBin.Scripts;

/// <summary>One original bin/MultiStart.bin record. Gold 460CF4 reads 48 records
/// into interpreter+1C; 4655D0 selects ordinary rows 0..15 or campaign rows 16..31.
/// The last 16 rows are retained without assigning an unverified purpose.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredMultiplayerStartPositionLayout
{
    public const int SerializedSize = 16;
    public const int SlotsPerTable = 16;
    public const int RecordCount = 48;
    public const int FileSize = SerializedSize * RecordCount;
    // The selector copies these fields only; alignment bytes are not position data.
    // A zero authored sector is retained; placement/sector resolution is separate.
    [FieldOffset(0)] public readonly ushort Sector;
    [FieldOffset(4)] public readonly int X;
    [FieldOffset(8)] public readonly int Y;
    [FieldOffset(12)] public readonly byte Surface;
}
