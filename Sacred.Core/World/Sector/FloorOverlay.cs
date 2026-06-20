namespace Sacred.Core.World.Sector;

public readonly record struct FloorOverlay(
    uint Metadata0,
    uint TileOrBlendRef,
    uint PrimaryTileId,
    uint SecondaryTileId,
    uint Metadata8,
    int ChainDepth);