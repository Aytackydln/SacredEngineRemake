namespace Sacred.Core.World.Elevation;

/// <summary>
/// Signed visual Y offsets stored at WLDX offsets 0x10-0x13. Sacred.exe applies these
/// directly to the four rendered terrain vertices; on liquid tiles the same authored
/// values also control per-corner opacity.
/// </summary>
public readonly record struct TerrainVisualElevationTile(
    sbyte SouthWest,
    sbyte NorthWest,
    sbyte NorthEast,
    sbyte SouthEast)
{
    /// <summary>Whether the four values deform the diamond instead of translating it rigidly.</summary>
    public bool IsDeformed =>
        SouthWest != NorthWest ||
        SouthWest != NorthEast ||
        SouthWest != SouthEast;
}
