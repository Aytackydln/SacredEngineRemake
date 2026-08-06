namespace Sacred.Core.World.Elevation;

/// <summary>
/// The four signed elevation samples serialized at offsets 0x18-0x1B of a WLDX tile,
/// retained in their north-to-south row-major order.
/// World Y increases southward, matching the sector tile grid.
/// </summary>
public readonly record struct TerrainElevationTile(
    sbyte NorthWest,
    sbyte NorthEast,
    sbyte SouthWest,
    sbyte SouthEast);
