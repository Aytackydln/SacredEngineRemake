using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pak.Tiles;

/// <summary>Known prefix of one tile definition stored in tiles.pak.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct TilePakEntryLayout
{
    /// <summary>Minimum serialized bytes required for the fields currently mapped.</summary>
    public const int SerializedSize = 0x28;

    /// <summary>Null-terminated tile resource name encoded as ASCII.</summary>
    [FieldOffset(0x00)]
    [BinaryString("ResourceName", 0x20, "ASCII")]
    private readonly byte _resourceName;

    /// <summary>Tile number used by world tile references.</summary>
    [FieldOffset(0x24)]
    public readonly uint TileNumber;
}
