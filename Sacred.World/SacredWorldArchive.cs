using Sacred.Assets.World.Floor;
using Sacred.Assets.World.Static;
using Sacred.Core.World;
using Sacred.Core.World.Elevation;
using Sacred.Core.World.Pathing;
using Sacred.Core.World.Sector;
using Sacred.Core.World.Stairs;

namespace Sacred.World;

public sealed class SacredWorldArchive : IDisposable
{
    private const int SectorW = Sector.TileCount;
    private const int SectorH = Sector.TileCount;

    private const int KeyxAbsoluteBias = 0x19;
    private const byte LiquidSurfaceTypeMask = 0xF0;
    private const byte LiquidSurfaceType90 = 0x90;
    private const byte LiquidSurfaceTypeA0 = 0xA0;
    private const int FloorChainMaxDepth = 128;
    private const int StaticChainMaxDepth = 4096;
    private static readonly SectorCoord BellevueSector = new(52, 38);

    private readonly FloorPakArchive _floorPak;
    private readonly StaticPakArchive _staticPak;
    private readonly WldxLoader _wldxLoader;
    private readonly Dictionary<uint, KeyxSectorRecord> _entriesById = new();
    private readonly Dictionary<SectorCoord, uint> _sectorIdByGrid = new();

    private readonly Dictionary<SectorCoord, Task<Sector>> _sectorLoadTasks = new();
    private readonly Dictionary<SectorCoord, Sector> _loadedSectors = new();
    private readonly List<IndoorTileGroup> _loadedIndoorGroups = [];
    private readonly object _sectorLoadTaskLock = new();
    private readonly object _sectorLoadLock = new();
    private bool _disposed;
    private WorldZoneMap _zoneMap = null!;

    public SectorCoord StartSector { get; private set; }
    public SacredStairsMap StairsMap { get; }

    public WorldZone GetZone(float worldX, float worldY) =>
        _zoneMap.GetZone(new SectorCoord(
            (int)MathF.Floor(worldX / SectorW),
            (int)MathF.Floor(worldY / SectorH)));

    public bool TryGetMinimapTextureName(SectorCoord coord, out string textureName)
    {
        if (!_sectorIdByGrid.ContainsKey(coord))
        {
            textureName = string.Empty;
            return false;
        }

        textureName = $"MINIMAP{coord.X:D3}{coord.Y:D3}.TGA";
        return true;
    }

    private SacredWorldArchive(
        byte[] keyxData,
        FileStream wldxStream,
        FloorPakArchive floorPak,
        StaticPakArchive staticPak,
        SacredStairsMap stairsMap)
    {
        _floorPak = floorPak;
        _staticPak = staticPak;
        _wldxLoader = new WldxLoader(wldxStream);
        StairsMap = stairsMap;
        LoadKeyx(keyxData);
        StartSector = _sectorIdByGrid.ContainsKey(BellevueSector)
            ? BellevueSector
            : FirstSectorOrZero();
    }

    internal static SacredWorldArchive Create(
        byte[] keyxData,
        FileStream wldxStream,
        FloorPakArchive floorPak,
        StaticPakArchive staticPak,
        SacredStairsMap stairsMap) =>
        new(keyxData, wldxStream, floorPak, staticPak, stairsMap);

    public async Task<Sector?> TryLoadSector(SectorCoord coord)
    {
        if (!_sectorIdByGrid.TryGetValue(coord, out var sectorId))
            return null;

        Task<Sector> loadTask;
        lock (_sectorLoadTaskLock)
        {
            if (!_sectorLoadTasks.TryGetValue(coord, out loadTask!))
            {
                var entry = _entriesById[sectorId];
                loadTask = Task.Run(() => LoadSector(coord, sectorId, entry));
                _sectorLoadTasks.Add(coord, loadTask);
            }
        }

        return await loadTask.ConfigureAwait(false);
    }

    private Sector LoadSector(SectorCoord coord, uint sectorId, KeyxSectorRecord entry)
    {
        lock (_sectorLoadLock)
        {
            var payload = _wldxLoader.LoadSector(sectorId, entry);
            var sector = CreateSector(coord, entry, payload.OutdoorTiles);
            LoadIndoorTileGroups(sector.IndoorTileGroups, coord, payload.IndoorGroups);
            AssociateLoadedIndoorGroups(sector);
            if (payload.IndoorGroups.Count > 0)
                Console.WriteLine($"Sector loaded: {coord.X},{coord.Y}, indoor groups={payload.IndoorGroups.Count}.");
            return sector;
        }
    }

    private void AssociateLoadedIndoorGroups(Sector newlyLoadedSector)
    {
        foreach (var group in _loadedIndoorGroups)
            if (Intersects(group, newlyLoadedSector.Coord))
                newlyLoadedSector.IndoorTileGroups.Add(group);

        foreach (var group in newlyLoadedSector.IndoorTileGroups.Groups)
        {
            if (_loadedIndoorGroups.All(existing => existing.Id != group.Id))
                _loadedIndoorGroups.Add(group);
            foreach (var loadedSector in _loadedSectors.Values)
                if (Intersects(group, loadedSector.Coord))
                    loadedSector.IndoorTileGroups.Add(group);
        }

        _loadedSectors[newlyLoadedSector.Coord] = newlyLoadedSector;
    }

    private static bool Intersects(IndoorTileGroup group, SectorCoord coord) =>
        group.WorldX < (coord.X + 1) * SectorW && group.WorldX + group.Width > coord.X * SectorW &&
        group.WorldY < (coord.Y + 1) * SectorH && group.WorldY + group.Height > coord.Y * SectorH;

    private Sector CreateSector(SectorCoord coord, KeyxSectorRecord entry, byte[] tiles)
    {
        var ground = new TileLayer(SectorW, SectorH);
        var floorOverlays = new FloorOverlayLayer(SectorW, SectorH);
        var liquidSurfaces = new LiquidSurfaceLayer();
        var staticObjects = new StaticObjectLayer();
        var stairsCells = new StairsCellLayer();
        var indoorTileGroups = new IndoorTileGroupLayer();
        var pathing = new WorldPathingLayer(SectorW, SectorH);
        var elevation = new TerrainElevationLayer(SectorW, SectorH);
        var staticTileVisits = new List<StaticTileVisit>();
        for (var y = 0; y < SectorH; y++)
        for (var x = 0; x < SectorW; x++)
        {
                var tileOffset = (y * SectorW + x) * WldxTileRecord.Size;
                var tile = WldxTileRecord.FromBytes(tiles.AsSpan(tileOffset, WldxTileRecord.Size));
                ground[x, y] = tile.GroundTileId;
                pathing[x, y] = new WorldPathTile(tile.PathFlags, tile.TypeAndSurface);
                elevation[x, y] = new TerrainElevationTile(
                    tile.ElevationNorthWest,
                    tile.ElevationNorthEast,
                    tile.ElevationSouthWest,
                    tile.ElevationSouthEast);
                if (tile.StaticChainHeadId != 0)
                {
                    var worldX = coord.X * SectorW + x;
                    var worldY = coord.Y * SectorH + y;
                    staticTileVisits.Add(new StaticTileVisit(worldX + worldY, worldY, worldX, tile.StaticChainHeadId));
                }

                LoadFloorOverlayChain(floorOverlays, x, y, tile.FloorChainHeadId);
                LoadLiquidSurface(liquidSurfaces, x, y, tile, entry);
        }

        LoadStaticObjectChains(staticObjects, staticTileVisits);
        LoadStairsCells(stairsCells, coord);
        return new Sector(
            coord,
            entry.Zone,
            ground,
            floorOverlays,
            liquidSurfaces,
            staticObjects,
            stairsCells,
            indoorTileGroups,
            pathing,
            elevation);
    }

    private void LoadIndoorTileGroups(
        IndoorTileGroupLayer layer,
        SectorCoord ownerCoord,
        IReadOnlyList<WldxIndoorGroupPayload> payloads)
    {
        for (var groupIndex = 0; groupIndex < payloads.Count; groupIndex++)
        {
            var payload = payloads[groupIndex];
            var pathing = new WorldPathingLayer(payload.Width, payload.Height);
            var presence = new IndoorTilePresenceLayer(payload.Width, payload.Height);
            var triggers = new List<IndoorTriggerTile>();

            for (var localY = 0; localY < payload.Height; localY++)
            for (var localX = 0; localX < payload.Width; localX++)
            {
                var tileOffset = (localY * payload.Width + localX) * WldxTileRecord.Size;
                var tileBytes = payload.Tiles.AsSpan(tileOffset, WldxTileRecord.Size);
                var tile = WldxTileRecord.FromBytes(tileBytes);
                var pathTile = new WorldPathTile(tile.PathFlags, tile.TypeAndSurface);
                pathing[localX, localY] = pathTile;
                presence[localX, localY] = HasAuthoredData(tileBytes);

                // Older indoor sections commonly omit the Trigger flag on their door cells.
                // Path type 9 is the stable authored door discriminator in both the
                // outdoor and indoor grids, so retain it as a trigger regardless.
                if (pathTile.Type == 9 || (tile.PathFlags & WorldPathFlags.Trigger) != 0)
                {
                    triggers.Add(new IndoorTriggerTile(
                        payload.WorldX + localX,
                        payload.WorldY + localY,
                        pathing[localX, localY]));
                }
            }

            byte surfaceLevel = 1;
            for (var previousIndex = 0; previousIndex < groupIndex; previousIndex++)
            {
                var previous = payloads[previousIndex];
                if (previous.WorldX == payload.WorldX && previous.WorldY == payload.WorldY &&
                    previous.Width == payload.Width && previous.Height == payload.Height)
                {
                    surfaceLevel++;
                }
            }

            layer.Add(new IndoorTileGroup(
                new IndoorTileGroupId(ownerCoord, groupIndex),
                payload.WorldX,
                payload.WorldY,
                payload.Width,
                payload.Height,
                payload.Kind,
                surfaceLevel,
                pathing,
                presence,
                triggers));
        }
    }

    private static bool HasAuthoredData(ReadOnlySpan<byte> tileBytes)
    {
        foreach (var value in tileBytes)
            if (value != 0)
                return true;
        return false;
    }

    private void LoadStairsCells(StairsCellLayer layer, SectorCoord coord)
    {
        var worldX = coord.X * SectorW;
        var worldY = coord.Y * SectorH;
        foreach (var cell in StairsMap.EnumerateCells(worldX, worldY, SectorW, SectorH))
            layer.Add(cell);
    }

    private void LoadFloorOverlayChain(FloorOverlayLayer floorOverlays, int x, int y, uint floorId)
    {
        HashSet<uint>? seen = null;
        var depth = 0;
        while (floorId != 0 && depth < FloorChainMaxDepth)
        {
            seen ??= [];
            if (!seen.Add(floorId))
                break;

            var record = _floorPak.Get(floorId);
            if (record is null)
                break;

            if (record.Value.PrimaryTileId != 0)
            {
                floorOverlays.Add(x, y, new FloorOverlay(
                    record.Value.Unknown0,
                    record.Value.TileOrBlendRef,
                    record.Value.PrimaryTileId,
                    record.Value.SecondaryTileId,
                    record.Value.Unknown8,
                    depth));
            }

            floorId = record.Value.NextFloorId;
            depth++;
        }
    }

    private void LoadStaticObjectChains(StaticObjectLayer staticObjects, List<StaticTileVisit> staticTileVisits)
    {
        staticTileVisits.Sort(static (left, right) =>
        {
            var depth = left.Depth.CompareTo(right.Depth);
            if (depth != 0)
                return depth;

            var worldY = left.WorldY.CompareTo(right.WorldY);
            return worldY != 0 ? worldY : left.WorldX.CompareTo(right.WorldX);
        });

        var reached = new HashSet<uint>();
        foreach (var visit in staticTileVisits)
        {
            var localSeen = new HashSet<uint>();
            var staticId = visit.StaticHeadId;
            var depth = 0;
            while (staticId != 0 && depth < StaticChainMaxDepth)
            {
                if (!localSeen.Add(staticId))
                    break;

                var record = _staticPak.Get(staticId);
                if (record is null)
                    break;

                if (!reached.Add(staticId))
                    break;

                staticObjects.Add(new StaticWorldObject(
                    staticId,
                    record.Value.TypeId,
                    record.Value.Flags,
                    record.Value.SectorId,
                    record.Value.ProjectedX,
                    record.Value.ProjectedY,
                    record.Value.NextStaticId,
                    record.Value.SurfaceRenderLayer,
                    record.Value.SpriteParam2E,
                    record.Value.SpriteParam2F,
                    record.Value.OrientationOrFrame,
                    record.Value.AnimationFrameDurationTicks,
                    record.Value.AnimationFrameCount,
                    visit.Depth,
                    visit.WorldY,
                    visit.WorldX,
                    depth,
                    staticObjects.Count));

                staticId = record.Value.NextStaticId;
                depth++;
            }
        }
    }

    private static void LoadLiquidSurface(
        LiquidSurfaceLayer liquidSurfaces,
        int x,
        int y,
        WldxTileRecord tile,
        KeyxSectorRecord entry)
    {
        var surfaceType = (byte)(tile.TypeAndSurface & LiquidSurfaceTypeMask);
        if (surfaceType is not LiquidSurfaceType90 and not LiquidSurfaceTypeA0)
            return;

        var styleId = surfaceType == LiquidSurfaceType90 ? entry.Style90 : entry.StyleA0;
        liquidSurfaces.Add(new LiquidSurface(
            x,
            y,
            tile.TypeAndSurface,
            styleId,
            tile.LiquidAlphaLeft,
            tile.LiquidAlphaTop,
            tile.LiquidAlphaRight,
            tile.LiquidAlphaBottom));
    }

    private void LoadKeyx(byte[] data)
    {
        if (data.Length < KeyxSectorRecord.FileHeaderSize)
            throw new InvalidDataException("sectors.keyx is too small to contain a header.");

        var count = ReadEntryCount(data);
        var rawEntries = new List<KeyxSectorRecord>(count);
        for (var i = 0; i < count; i++)
        {
            var offset = KeyxSectorRecord.FileHeaderSize + i * KeyxSectorRecord.Size;
            if (offset + KeyxSectorRecord.Size > data.Length)
                break;

            rawEntries.Add(KeyxSectorRecord.FromBytes(data.AsSpan(offset, KeyxSectorRecord.Size)));
        }

        var scale = InferKeyxPositionScale(rawEntries);
        foreach (var entry in rawEntries)
        {
            var originX = RoundToSectorOrigin((entry.RawX + KeyxAbsoluteBias) * scale);
            var originY = RoundToSectorOrigin((entry.RawY + KeyxAbsoluteBias) * scale);
            var coord = new SectorCoord(originX / SectorW, originY / SectorH);

            _entriesById[entry.Id] = entry;
            _sectorIdByGrid[coord] = entry.Id;
        }

        _zoneMap = new WorldZoneMap(_sectorIdByGrid.Select(pair =>
            new KeyValuePair<SectorCoord, WorldZone>(pair.Key, _entriesById[pair.Value].Zone)));
        Console.WriteLine(
            $"World zones loaded: outdoors={_zoneMap.OutdoorSectorCount} sectors, caves={_zoneMap.CaveSectorCount} sectors.");
    }

    private static int ReadEntryCount(byte[] data)
    {
        var count32 = BitConverter.ToUInt32(data, 4);
        var count16 = BitConverter.ToUInt16(data, 4);
        var maxCount = Math.Max(0, (data.Length - KeyxSectorRecord.FileHeaderSize) / KeyxSectorRecord.Size);
        if (count32 <= maxCount)
            return (int)count32;
        if (count16 <= maxCount)
            return count16;

        throw new InvalidDataException($"Cannot determine sectors.keyx entry count. count16={count16}, count32={count32}, max={maxCount}");
    }

    private static float InferKeyxPositionScale(List<KeyxSectorRecord> entries)
    {
        var diffs = new List<int>();
        CollectDiffs(entries, static entry => entry.RawX, diffs);
        CollectDiffs(entries, static entry => entry.RawY, diffs);
        if (diffs.Count == 0)
            throw new InvalidDataException("Cannot infer KEYX absolute-position scale from sector positions.");

        var min = diffs[0];
        foreach (var diff in diffs)
            if (diff < min)
                min = diff;

        return SectorW / (float)min;
    }

    private static void CollectDiffs(
        List<KeyxSectorRecord> entries,
        Func<KeyxSectorRecord, int> selectValue,
        List<int> diffs)
    {
        var values = new SortedSet<int>();
        foreach (var entry in entries)
            values.Add(selectValue(entry));

        int? previous = null;
        foreach (var value in values)
        {
            if (previous is { } p && value > p)
                diffs.Add(value - p);
            previous = value;
        }
    }

    private static int RoundToSectorOrigin(float value) =>
        (int)MathF.Round(value / SectorW) * SectorW;

    private SectorCoord FirstSectorOrZero()
    {
        foreach (var coord in _sectorIdByGrid.Keys)
            return coord;

        return new SectorCoord(3200, 2600);
    }

    private readonly record struct StaticTileVisit(int Depth, int WorldY, int WorldX, uint StaticHeadId);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _wldxLoader.Dispose();
        _floorPak.Dispose();
        _staticPak.Dispose();
    }
}
