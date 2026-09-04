namespace Sacred.Core.World.Pathing;

/// <summary>Known authored world-state bits stored at WLDX tile offset 0x1E.</summary>
[Flags]
public enum WorldPathFlags : byte
{
    None = 0,
    /// <summary>Tile belongs to an authored indoor area.</summary>
    Indoor = 0x01,
    /// <summary>Tile belongs to an authored town area.</summary>
    Town = 0x02,
    Trigger = 0x04,
    RuntimeBlocked = 0x08,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal byte value.</summary>
    Byte10 = 0x10,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal byte value.</summary>
    Byte20 = 0x20,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal byte value.</summary>
    Byte40 = 0x40,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal byte value.</summary>
    Byte80 = 0x80,
}

/// <summary>
/// Raw navigation metadata stored at offsets 0x1E and 0x1F of a sectors.wldx tile.
/// Byte 0x1F is exposed through <see cref="Properties"/>, which owns all packed-bit checks.
/// </summary>
public readonly record struct WorldPathTile(WorldPathFlags Flags, WldxTileProperties Properties)
{
    public WldxTileFlags TileFlags => Properties.TileFlags;
    public WldxTerrainSurface TerrainSurface => Properties.TerrainSurface;
    public bool IsTown => Flags.HasFlag(WorldPathFlags.Town);
    public bool IsEntrance => Properties.IsEntrance;
    public bool IsEntranceBoundary => Properties.IsEntranceBoundary;
    public bool IsIndoorArea => Flags.HasFlag(WorldPathFlags.Indoor);

    /// <summary>
    /// Matches Sacred's tile acceptance predicate: either exact authored low-nibble
    /// blocker value rejects the tile, as does the live-state blocker in byte 0x1E.
    /// Composite doorway values reuse blocker bits but remain traversable.
    /// </summary>
    public bool IsBlocked => Properties.BlocksMovement || Flags.HasFlag(WorldPathFlags.RuntimeBlocked);
}
