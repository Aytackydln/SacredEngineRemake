using Sacred.Core.World.Elevation;
using Sacred.Core.World.Pathing;

namespace Sacred.Core.World.Sector;

public sealed class Sector(
    SectorCoord coord,
    TileLayer ground,
    FloorOverlayLayer floorOverlays,
    LiquidSurfaceLayer liquidSurfaces,
    StaticObjectLayer staticObjects,
    StairsCellLayer stairsCells,
    WorldPathingLayer pathing,
    TerrainElevationLayer elevation)
{
    public const int TileCount = 64;

    public SectorCoord Coord { get; } = coord;
    public TileLayer Ground { get; } = ground;
    public FloorOverlayLayer FloorOverlays { get; } = floorOverlays;
    public LiquidSurfaceLayer LiquidSurfaces { get; } = liquidSurfaces;
    public StaticObjectLayer StaticObjects { get; } = staticObjects;
    public StairsCellLayer StairsCells { get; } = stairsCells;
    public WorldPathingLayer Pathing { get; } = pathing;
    public TerrainElevationLayer Elevation { get; } = elevation;
}
