using System.Runtime.InteropServices;

namespace Sacred.Core.World;

/// <summary>One linked floor-overlay record stored in <c>Floor.pak</c>.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct FloorOverlayRecord(uint Unknown0, uint TileOrBlendRef, uint Unknown8, uint NextFloorId)
{
    private const uint PrimaryTileMask = 0x1FFFF;
    private const int SecondaryTileShift = 17;
    private const uint SecondaryTileMask = 0x7FFF;

    /// <summary>Unresolved value at byte offset 0x00.</summary>
    public readonly uint Unknown0 = Unknown0;
    /// <summary>Packed primary and secondary tile references.</summary>
    public readonly uint TileOrBlendRef = TileOrBlendRef;
    /// <summary>Unresolved value at byte offset 0x08.</summary>
    public readonly uint Unknown8 = Unknown8;
    /// <summary>Identifier of the next overlay in this tile's linked list.</summary>
    public readonly uint NextFloorId = NextFloorId;

    public uint PrimaryTileId => GetPrimaryTileId(TileOrBlendRef);
    public uint SecondaryTileId => GetSecondaryTileId(TileOrBlendRef);

    private static uint GetPrimaryTileId(uint tileOrBlendRef) => tileOrBlendRef & PrimaryTileMask;

    private static uint GetSecondaryTileId(uint tileOrBlendRef) =>
        (tileOrBlendRef >> SecondaryTileShift) & SecondaryTileMask;
}
