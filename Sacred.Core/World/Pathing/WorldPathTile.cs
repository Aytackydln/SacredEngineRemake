namespace Sacred.Core.World.Pathing;

/// <summary>Known navigation-state bits stored at WLDX tile offset 0x1E.</summary>
[Flags]
public enum WorldPathFlags : byte
{
    None = 0,
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

    public byte Type => (byte)(TypeAndSurface & TypeMask);

    /// <summary>
    /// Matches Sacred's tile acceptance predicate: path types 1 and 2 are authored
    /// blockers; flag 0x08 is reserved for a blocker added to the live tile state.
    /// </summary>
    public bool IsBlocked => Type is 1 or 2 || (Flags & WorldPathFlags.RuntimeBlocked) != 0;
}
