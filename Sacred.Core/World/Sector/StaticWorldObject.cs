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
    byte AnimationFrameDurationTicks,
    byte AnimationFrameCount,
    int TileDepth,
    int TileWorldY,
    int TileWorldX,
    int ChainDepth,
    int InsertionOrder)
{
    public byte MiniObjectSourceXOrAtlasColumns => SpriteParam2E;
    public byte MiniObjectSourceYOrAtlasRows => SpriteParam2F;
    public byte MiniObjectSourceSize => OrientationOrFrame;
    public byte MiniObjectFrameDurationTicks => AnimationFrameDurationTicks;
    public byte MiniObjectFrameCount => AnimationFrameCount;
    public bool UsesAlternateSurface =>
        (Flags & Sacred.Core.World.StaticObjectRecord.AlternateSurfaceFlag) != 0;
}
