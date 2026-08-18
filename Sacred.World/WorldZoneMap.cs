using Sacred.Core.World.Sector;

namespace Sacred.World;

/// <summary>Resolves authored KEYX environments, including gaps without a sector record.</summary>
internal sealed class WorldZoneMap
{
    private const int EmptySectorSearchRadius = 2;
    private readonly Dictionary<SectorCoord, WorldZone> _zones;

    public WorldZoneMap(IEnumerable<KeyValuePair<SectorCoord, WorldZone>> sectors)
    {
        _zones = sectors.ToDictionary();
        OutdoorSectorCount = _zones.Count(static pair => pair.Value == WorldZone.Outdoors);
        CaveSectorCount = _zones.Count(static pair => pair.Value == WorldZone.Cave);
    }

    public int OutdoorSectorCount { get; }
    public int CaveSectorCount { get; }

    public WorldZone GetZone(SectorCoord coord)
    {
        if (_zones.TryGetValue(coord, out var directZone))
            return directZone;

        // Some dungeon layouts deliberately contain an absent sector record. Prefer the
        // nearest authored environment so lighting stays stable while traversing the gap.
        for (var distance = 1; distance <= EmptySectorSearchRadius; distance++)
        {
            var caveCount = 0;
            var outdoorCount = 0;
            for (var y = -distance; y <= distance; y++)
            for (var x = -distance; x <= distance; x++)
            {
                if (Math.Max(Math.Abs(x), Math.Abs(y)) != distance ||
                    !_zones.TryGetValue(new SectorCoord(coord.X + x, coord.Y + y), out var zone))
                {
                    continue;
                }

                if (zone == WorldZone.Cave)
                    caveCount++;
                else
                    outdoorCount++;
            }

            if (caveCount + outdoorCount > 0)
                return caveCount >= outdoorCount ? WorldZone.Cave : WorldZone.Outdoors;
        }

        return WorldZone.Outdoors;
    }
}
