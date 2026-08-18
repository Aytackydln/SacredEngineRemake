namespace Sacred.Core.World.Sector;

/// <summary>
/// Authored sector environment and edge flags stored at KEYX record offset 0x1CC.
/// The boundary flags identify an absent neighbor on the corresponding map edge.
/// </summary>
[Flags]
public enum SectorEnvironmentFlags : byte
{
    None = 0,
    Dungeon = 0x04,
    NorthBoundary = 0x10,
    EastBoundary = 0x20,
    SouthBoundary = 0x40,
    WestBoundary = 0x80,
}
