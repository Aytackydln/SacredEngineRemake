using System.Runtime.InteropServices;

namespace Sacred.Core.World;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct FloorOverlayRecord(uint Unknown0, uint TileOrBlendRef, uint Unknown8, uint NextFloorId)
{
    private const uint PrimaryTileMask = 0x1FFFF;
    private const int SecondaryTileShift = 17;
    private const uint SecondaryTileMask = 0x7FFF;

    public readonly uint Unknown0 = Unknown0;
    public readonly uint TileOrBlendRef = TileOrBlendRef;
    public readonly uint Unknown8 = Unknown8;
    public readonly uint NextFloorId = NextFloorId;

    public uint PrimaryTileId => GetPrimaryTileId(TileOrBlendRef);
    public uint SecondaryTileId => GetSecondaryTileId(TileOrBlendRef);

    private static uint GetPrimaryTileId(uint tileOrBlendRef) => tileOrBlendRef & PrimaryTileMask;

    private static uint GetSecondaryTileId(uint tileOrBlendRef) =>
        (tileOrBlendRef >> SecondaryTileShift) & SecondaryTileMask;
}
