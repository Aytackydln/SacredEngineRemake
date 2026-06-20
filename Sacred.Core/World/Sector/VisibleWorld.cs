namespace Sacred.Core.World.Sector;

public sealed record VisibleWorld(SectorCoord CenterSector, IReadOnlyList<Sector> Sectors, int LoadingSectors)
{
    public static readonly VisibleWorld Empty = new(new SectorCoord(0, 0), [], 0);
}