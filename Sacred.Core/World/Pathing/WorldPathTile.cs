namespace Sacred.Core.World.Pathing;

/// <summary>Known navigation-state bits stored at WLDX tile offset 0x1E.</summary>
[Flags]
public enum WorldPathFlags : byte
{
    None = 0,
    /// <summary>Elevated actor surface without lateral screen projection.</summary>
    VerticalElevationSurface = 0x01,
    /// <summary>Elevated actor surface whose authored screen projection points right.</summary>
    RightProjectedElevationSurface = 0x02,
    Trigger = 0x04,
    RuntimeBlocked = 0x08,
}

/// <summary>
/// Raw navigation metadata stored at offsets 0x1E and 0x1F of a sectors.wldx tile.
/// The high nibble of <see cref="TypeAndSurface"/> is also used by surface rendering.
/// </summary>
public readonly record struct WorldPathTile(WorldPathFlags Flags, byte TypeAndSurface)
{
    public const byte TypeMask = 0x0F;
    public const byte SuppressTerrainTessellationMask = 0x10;

    public byte Type => (byte)(TypeAndSurface & TypeMask);
    /// <summary>
    /// Lateral projection selected for elevated surface vertices. Flag 0x01 raises
    /// actors vertically without moving them sideways; flag 0x02 also projects right.
    /// Shared vertices blend this tile-authored state.
    /// </summary>
    public int ElevationHorizontalDirection
    {
        get
        {
            if ((Flags & WorldPathFlags.RightProjectedElevationSurface) != 0)
                return 1;
            return 0;
        }
    }
    /// <summary>
    /// Whether the terrain renderer applies the four WLDX elevation samples. Bit 0x10
    /// keeps those samples available to actors while static scenery carries the surface art.
    /// </summary>
    public bool UsesTerrainTessellation =>
        (TypeAndSurface & SuppressTerrainTessellationMask) == 0;

    /// <summary>
    /// Matches Sacred's tile acceptance predicate: path types 1 and 2 are authored
    /// blockers; flag 0x08 is reserved for a blocker added to the live tile state.
    /// </summary>
    public bool IsBlocked => Type is 1 or 2 || (Flags & WorldPathFlags.RuntimeBlocked) != 0;
}
