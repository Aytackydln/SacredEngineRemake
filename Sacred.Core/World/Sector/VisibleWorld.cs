namespace Sacred.Core.World.Sector;

public sealed record VisibleWorld(SectorCoord CenterSector, IReadOnlyList<Sector> Sectors, int LoadingSectors)
{
    public IReadOnlyList<Sector> PreloadedSectors { get; init; } = [];
    public SectorCoord? PrefetchCenterSector { get; init; }
    public int LoadingPreloadedSectors { get; init; }
    public static readonly VisibleWorld Empty = new(new SectorCoord(0, 0), [], 0);
}
