using System.Runtime.CompilerServices;
using Sacred.Core.World.Pathing;
using Sacred.Core.World.Sector;

namespace Sacred.World.Networking;

/// <summary>Copies only mutable navigation planes. Archive geometry stays shared; session
/// tile flags and later-arriving indoor grids never mutate cached offline sector data.</summary>
internal sealed class SacredNetworkSectorCopies
{
    private readonly ConditionalWeakTable<Sector, Sector> _sources = new();
    private readonly ConditionalWeakTable<IndoorTileGroup, IndoorTileGroup> _groups = new();
    public Sector Create(Sector source)
    {
        var copy = new Sector(source.Coord, source.EnvironmentFlags, source.Ground, source.FloorOverlays,
            source.LiquidSurfaces, source.StaticObjects, source.WorldObjects, source.StairsCells, new(),
            CopyPlane(source.Pathing), source.VisualElevation, source.Elevation, source.BakedLight)
            { IndoorAnchors = source.IndoorAnchors, TileAnchorDeltas = source.TileAnchorDeltas, StaticChainHeads = source.StaticChainHeads };
        _sources.Add(copy, source);
        return copy;
    }
    public void Synchronize(Sector copy)
    {
        if (!_sources.TryGetValue(copy, out var source)) return;
        var known = copy.IndoorTileGroups.Groups.Select(group => group.Id).ToHashSet();
        foreach (var group in source.IndoorTileGroups.Groups)
            if (known.Add(group.Id))
                copy.IndoorTileGroups.Add(_groups.GetValue(group, static source => new(source.Id, source.WorldX, source.WorldY,
                    source.Width, source.Height, source.Kind, source.SurfaceLevel, CopyPlane(source.Pathing), source.Presence, source.Triggers)
                    { Elevation = source.Elevation, BaseHeight = source.BaseHeight, BuildingAnchor = source.BuildingAnchor }));
    }
    private static WorldPathingLayer CopyPlane(WorldPathingLayer source)
    {
        var copy = new WorldPathingLayer(source.Width, source.Height);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++) copy[x, y] = source[x, y];
        return copy;
    }
}
