using Sacred.Core.World;
using Sacred.Core.World.Pathing;
using Sacred.Core.World.Sector;

namespace Sacred.World;

/// <summary>Selects the shared ground navigation plane or one isolated upper-floor grid.</summary>
internal sealed class WorldNavigationTileSource(WorldStreamer worldStreamer, Func<IndoorTileGroup?> activeIndoorGroup)
{
    private readonly Dictionary<SectorCoord, Sector> _sectors = new(9);
    private readonly Dictionary<long, WorldPathTile> _groundFloorTiles = [];
    private VisibleWorld? _cachedWorld;

    public bool TryGetTile(int x, int y, byte surfaceLevel, out WorldPathTile tile)
    {
        if (surfaceLevel > 1)
        {
            if (activeIndoorGroup() is { } upper &&
                upper.TryGetLocalTile(x, y, out var indoorX, out var indoorY))
            {
                tile = upper.Pathing[indoorX, indoorY];
                return true;
            }
            tile = default;
            return false;
        }

        Refresh();
        var coord = new SectorCoord(FloorDiv(x, Sector.TileCount), FloorDiv(y, Sector.TileCount));
        var hasOutdoor = _sectors.TryGetValue(coord, out var sector);
        tile = hasOutdoor ? sector!.Pathing[x - coord.X * Sector.TileCount, y - coord.Y * Sector.TileCount] : default;
        if (_groundFloorTiles.TryGetValue(TileKey(x, y), out var indoor))
        {
            // Interior floors replace the exterior building blockers. Indoor walls that
            // overlap a walkable exterior approach belong to the interior view only.
            // Use the same plane before and after entry so an approach route stays valid.
            if (!indoor.IsBlocked || !hasOutdoor || tile.IsBlocked ||
                indoor.Flags.HasFlag(WorldPathFlags.RuntimeBlocked))
                tile = indoor;
            return true;
        }
        return hasOutdoor;
    }

    private void Refresh()
    {
        var world = worldStreamer.VisibleWorld;
        if (ReferenceEquals(world, _cachedWorld)) return;
        _cachedWorld = world;
        _sectors.Clear();
        _groundFloorTiles.Clear();
        var indexed = new HashSet<IndoorTileGroupId>();
        foreach (var sector in world.Sectors) _sectors[sector.Coord] = sector;
        foreach (var sector in world.Sectors)
        {
            foreach (var group in sector.IndoorTileGroups.Groups)
            {
                if (group.SurfaceLevel != 1 || !indexed.Add(group.Id)) continue;
                for (var y = 0; y < group.Height; y++)
                for (var x = 0; x < group.Width; x++)
                {
                    var worldX = group.WorldX + x;
                    var worldY = group.WorldY + y;
                    var coord = new SectorCoord(FloorDiv(worldX, Sector.TileCount), FloorDiv(worldY, Sector.TileCount));
                    var anchor = _sectors.TryGetValue(coord, out var outdoor)
                        ? outdoor.IndoorAnchors?[worldX - coord.X * Sector.TileCount, worldY - coord.Y * Sector.TileCount]
                        : null;
                    if (anchor is not null && group.BuildingAnchor == anchor &&
                        (group.Presence[x, y] || outdoor!.Pathing[worldX - coord.X * Sector.TileCount,
                            worldY - coord.Y * Sector.TileCount].Properties.Behavior == WldxTileBehavior.MovementBlockerA))
                        _groundFloorTiles[TileKey(worldX, worldY)] = group.Pathing[x, y];
                    else if (group.Presence[x, y])
                        _groundFloorTiles.TryAdd(TileKey(worldX, worldY), group.Pathing[x, y]);
                }
            }
        }
    }

    private static long TileKey(int x, int y) => ((long)x << 32) | (uint)y;
    private static int FloorDiv(int value, int divisor) =>
        value / divisor - (value < 0 && value % divisor != 0 ? 1 : 0);
}
