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
    private readonly Dictionary<uint, KeyxSectorRecord> _entriesById = new();
    private readonly Dictionary<SectorCoord, uint> _sectorIdByGrid = new();

    private readonly WldxLoader _wldxLoader;
    private readonly SemaphoreSlim _sectorLoadLock = new(1, 1);
    private bool _disposed;

    public SectorCoord StartSector { get; private set; }
    public SacredStairsMap StairsMap { get; }

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
        _wldxLoader = new WldxLoader(wldxStream);
        _floorPak = floorPak;
        _staticPak = staticPak;
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

        var entry = _entriesById[sectorId];

        await _sectorLoadLock.WaitAsync();
        try
        {
            // Keep only the tile block in memory, and serialize extraction with parsing so
            // queued sector loads cannot accumulate decompressed WLDX buffers.
            var tiles = await _wldxLoader.ReadSectorTiles(entry, sectorId);
            var ground = new TileLayer(SectorW, SectorH);
            var floorOverlays = new FloorOverlayLayer(SectorW, SectorH);
            var liquidSurfaces = new LiquidSurfaceLayer();
            var staticObjects = new StaticObjectLayer();
            var stairsCells = new StairsCellLayer();
            var pathing = new WorldPathingLayer(SectorW, SectorH);
            var elevation = new TerrainElevationLayer(SectorW, SectorH);
            var staticTileVisits = new List<StaticTileVisit>();
            for (var y = 0; y < SectorH; y++)
            for (var x = 0; x < SectorW; x++)
            {
                var tileOffset = (y * SectorW + x) * WldxTileRecord.Size;
                var tile = WldxTileRecord.FromBytes(tiles.AsSpan(tileOffset, WldxTileRecord.Size));
                ground[x, y] = tile.GroundTileId;
                pathing[x, y] = new WorldPathTile(tile.PathFlags, tile.SurfaceType);
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
                ground,
                floorOverlays,
                liquidSurfaces,
                staticObjects,
                stairsCells,
                pathing,
                elevation);
        }
        finally
        {
            _sectorLoadLock.Release();
        }
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
        var surfaceType = (byte)(tile.SurfaceType & LiquidSurfaceTypeMask);
        if (surfaceType is not LiquidSurfaceType90 and not LiquidSurfaceTypeA0)
            return;

        var styleId = surfaceType == LiquidSurfaceType90 ? entry.Style90 : entry.StyleA0;
        liquidSurfaces.Add(new LiquidSurface(
            x,
            y,
            tile.SurfaceType,
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
        _sectorLoadLock.Dispose();
    }
}
