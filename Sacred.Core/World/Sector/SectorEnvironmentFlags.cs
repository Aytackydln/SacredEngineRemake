namespace Sacred.Core.World.Sector;

/// <summary>
/// Authored sector environment and edge flags stored at KEYX record offset 0x1CC.
/// The boundary flags identify an absent neighbor on the corresponding map edge.
/// </summary>
[Flags]
public enum SectorEnvironmentFlags : byte
{
    None = 0,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal byte value.</summary>
    Byte01 = 0x01,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal byte value.</summary>
    Byte02 = 0x02,
    Dungeon = 0x04,
    /// <summary>Unresolved flag bit. The name preserves its raw hexadecimal byte value.</summary>
    Byte08 = 0x08,
    NorthBoundary = 0x10,
    EastBoundary = 0x20,
    SouthBoundary = 0x40,
    WestBoundary = 0x80,
}
