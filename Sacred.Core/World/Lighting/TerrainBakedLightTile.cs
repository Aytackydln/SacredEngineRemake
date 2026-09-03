namespace Sacred.Core.World.Lighting;

/// <summary>
/// The four baked WLDX terrain-brightness samples in north-to-south row-major order.
/// A value of 255 is fully lit; lower values form the original game's static,
/// directionally authored terrain occlusion.
/// </summary>
public readonly record struct TerrainBakedLightTile(
    byte NorthWest,
    byte NorthEast,
    byte SouthWest,
    byte SouthEast)
{
    public static TerrainBakedLightTile FullyLit => new(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
}
