namespace Sacred.Core.World.Sector;

public readonly record struct StaticWorldObject(
    uint StaticId,
    uint TypeId,
    uint Flags,
    ushort SectorId,
    int ProjectedX,
    int ProjectedY,
    uint NextStaticId,
    short SurfaceRenderLayer,
    byte SpriteParam2E,
    byte SpriteParam2F,
    byte OrientationOrFrame,
    int TileDepth,
    int TileWorldY,
    int TileWorldX,
    int ChainDepth,
    int InsertionOrder);
