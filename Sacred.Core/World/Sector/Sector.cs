using Sacred.Core.World.Elevation;
using Sacred.Core.World.Pathing;

namespace Sacred.Core.World.Sector;

public sealed class Sector(
    SectorCoord coord,
    WorldZone zone,
    TileLayer ground,
    FloorOverlayLayer floorOverlays,
    LiquidSurfaceLayer liquidSurfaces,
    StaticObjectLayer staticObjects,
    StairsCellLayer stairsCells,
    IndoorTileGroupLayer indoorTileGroups,
    WorldPathingLayer pathing,
    TerrainElevationLayer elevation)
{
    public const int TileCount = 64;

    public SectorCoord Coord { get; } = coord;
    public WorldZone Zone { get; } = zone;
    public TileLayer Ground { get; } = ground;
    public FloorOverlayLayer FloorOverlays { get; } = floorOverlays;
    public LiquidSurfaceLayer LiquidSurfaces { get; } = liquidSurfaces;
    public StaticObjectLayer StaticObjects { get; } = staticObjects;
    public StairsCellLayer StairsCells { get; } = stairsCells;
    public IndoorTileGroupLayer IndoorTileGroups { get; } = indoorTileGroups;
    public WorldPathingLayer Pathing { get; } = pathing;
    public TerrainElevationLayer Elevation { get; } = elevation;
}
