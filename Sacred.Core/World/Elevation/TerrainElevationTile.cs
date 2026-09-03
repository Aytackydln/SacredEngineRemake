namespace Sacred.Core.World.Elevation;

/// <summary>
/// The four signed gameplay-height samples serialized at offsets 0x18-0x1B of a WLDX tile,
/// retained in their north-to-south row-major order. Sacred.exe samples these for actor/world
/// height but does not use them to position the rendered terrain-image vertices.
/// World Y increases southward, matching the sector tile grid.
/// </summary>
public readonly record struct TerrainElevationTile(
    sbyte NorthWest,
    sbyte NorthEast,
    sbyte SouthWest,
    sbyte SouthEast);
