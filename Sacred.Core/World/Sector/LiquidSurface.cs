namespace Sacred.Core.World.Sector;

public readonly record struct LiquidSurface(
    int LocalX,
    int LocalY,
    WldxTileProperties TileProperties,
    byte StyleId,
    sbyte AlphaLeft,
    sbyte AlphaTop,
    sbyte AlphaRight,
    sbyte AlphaBottom)
{
    public WldxTerrainSurface SurfaceType => TileProperties.TerrainSurface;
    // These raw signed values are shared with terrain visual elevation. Sacred.exe negates
    // and scales them when producing the liquid vertex alpha.
    // Authored floor-overlay insertion depth used when splitting terrain below and above liquid.
    public byte FloorInsertionDepth => TileProperties.FloorInsertionDepth;
}
