using Sacred.Core.World.Sector;
using WorldSector = Sacred.Core.World.Sector.Sector;

namespace Sacred.Core.World;

/// <summary>Gold KEYX origin conversion at 0x638321..0x638358, followed by the grid builder at 0x638730.</summary>
public static class SacredSectorCoordinates
{
    public const int OriginBias = 25;
    public const float WorldUnitsPerTile = 53.66563034057617f;
    public const int GridWidth = 128;
    public static int ToTileOrigin(int raw) => (int)(unchecked(raw + OriginBias) / (double)WorldUnitsPerTile);
    public static SectorCoord ToGrid(KeyxSectorRecord record) =>
        new(ToTileOrigin(record.RawX) / WorldSector.TileCount, ToTileOrigin(record.RawY) / WorldSector.TileCount);
}
