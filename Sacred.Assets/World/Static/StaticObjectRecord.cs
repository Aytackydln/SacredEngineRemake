namespace Sacred.Assets.World.Static;

public readonly record struct StaticObjectRecord(
    uint StaticId,
    uint DescriptorType,
    uint PayloadSize,
    uint PayloadInstanceId,
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
    byte ElevationTier);