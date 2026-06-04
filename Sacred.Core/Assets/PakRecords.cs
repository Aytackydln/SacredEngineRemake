namespace Sacred.Core.Assets;

public readonly record struct FloorOverlayRecord(uint FloorId, uint TileOrBlendRef, uint NextFloorId)
{
    public uint PrimaryTileId => FloorPakData.PrimaryTileId(TileOrBlendRef);
    public uint SecondaryTileId => FloorPakData.SecondaryTileId(TileOrBlendRef);
}

public readonly record struct ItemTypeRecord(
    uint TypeId,
    uint DescriptorType,
    uint MixedBaseGroupId,
    uint GraphicRenderFlags,
    byte RenderClass);

public readonly record struct PlayerCharacterItemRecord(
    uint EntryId,
    uint ItemId,
    string ModelName);

public readonly record struct MixedCutoutRecord(
    uint MixedId,
    uint PieceIndex,
    string AtlasName,
    uint CutoutId,
    int Right,
    int Bottom,
    int Left,
    int Top,
    float Uv0,
    float Uv1,
    float Uv2,
    float Uv3);

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

public readonly record struct TileDefinition(string FileName, uint TileNumber)
{
    public static readonly TileDefinition Empty = new(string.Empty, 0);
}

public readonly record struct TexturePakRecord(
    string Name,
    long Offset,
    int Size,
    ushort Width,
    ushort Height,
    byte Type);
