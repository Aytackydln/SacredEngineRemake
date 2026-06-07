namespace Sacred.Assets.World.Floor;

public readonly record struct FloorOverlayRecord(uint FloorId, uint TileOrBlendRef, uint NextFloorId)
{
    public uint PrimaryTileId => FloorPakData.PrimaryTileId(TileOrBlendRef);
    public uint SecondaryTileId => FloorPakData.SecondaryTileId(TileOrBlendRef);
}