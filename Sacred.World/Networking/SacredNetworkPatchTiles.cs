using Sacred.Core.World;
using Sacred.Core.World.Pathing;
using Sacred.Core.World.Sector;

namespace Sacred.World.Networking;

/// <summary>Gold 0x6354D0/0x634C50 floor selection, using raw outdoor anchors and
/// Static.pak links. It does not infer a floor from bounding-box overlap.</summary>
internal sealed class SacredNetworkPatchTiles(Func<uint, StaticObjectRecord?> readStatic)
{
    public (WorldPathingLayer Plane, int X, int Y)? Resolve(SacredNetworkPatchTerrain terrain, int x, int y, ushort surface)
    {
        var sector = terrain.At(x, y);
        if (sector is null) return null;
        if (surface == 0) return (sector.Pathing, x % Sector.TileCount, y % Sector.TileCount);
        return ResolveFloor(terrain, x, y, surface);
    }
    /// <summary>Native floor lookup accepts level zero as the indoor parent, still requiring an indoor anchor.</summary>
    public (WorldPathingLayer Plane, int X, int Y)? ResolveFloor(SacredNetworkPatchTerrain terrain, int x, int y, ushort surface)
    {
        var sector = terrain.At(x, y);
        if (sector is null) return null;
        var delta = sector.TileAnchorDeltas?[x % Sector.TileCount, y % Sector.TileCount] ?? default;
        var ax = x + delta.X; var ay = y + delta.Y;
        var anchor = terrain.At(ax, ay);
        if (anchor is null || !anchor.Pathing[ax % Sector.TileCount, ay % Sector.TileCount].IsIndoorArea) return null;
        var id = anchor.StaticChainHeads?[ax % Sector.TileCount, ay % Sector.TileCount] ?? 0;
        var seen = new HashSet<uint>();
        StaticObjectRecord? parent = null;
        while (id != 0 && seen.Add(id) && readStatic(id) is { } record)
        {
            if (record.Flags.HasFlag(StaticObjectFlags.IndoorSurfaceParent)) { parent = record; break; }
            id = record.NextStaticId;
        }
        if (parent is not { } building) return null;
        id = surface == 0 ? id : building.IndoorSurfaceChainHeadId;
        for (var level = 1; level < surface; level++)
        {
            if (id == 0 || readStatic(id) is not { } child) return null;
            id = child.NextStaticId;
        }
        if (id == 0 || readStatic(id) is not { } floor || terrain.ById(floor.SectorId) is not { } owner) return null;
        if (floor.HeightLevelId == 0) return (sector.Pathing, x % Sector.TileCount, y % Sector.TileCount);
        var groupId = new IndoorTileGroupId(owner.Coord, floor.HeightLevelId - 1);
        var descriptor = owner.IndoorTileGroups.Groups.FirstOrDefault(group => group.Id == groupId);
        if (descriptor is { Width: 0, Height: 0 }) return (sector.Pathing, x % Sector.TileCount, y % Sector.TileCount);
        return descriptor is { } group && group.TryGetLocalTile(x, y, out var gx, out var gy) ? (group.Pathing, gx, gy) : null;
    }
}
