using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.World.Floor;
using Sacred.Assets.World.Static;
using Sacred.Core;
using Sacred.Core.World;

namespace Sacred.Engine.World;

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

    public SectorCoord StartSector { get; private set; }

    private SacredWorldArchive(string keyxPath, FileStream wldxStream, FloorPakArchive floorPak, StaticPakArchive staticPak)
    {
        _wldxLoader = new WldxLoader(wldxStream);
        _floorPak = floorPak;
        _staticPak = staticPak;
        LoadKeyx(keyxPath);
        StartSector = _sectorIdByGrid.ContainsKey(BellevueSector)
            ? BellevueSector
            : FirstSectorOrZero();
    }

    public static SacredWorldArchive Load(SacredGameDirectories directories)
    {
        var pakDirectory = Path.GetDirectoryName(directories.TexturesPakPath)
            ?? throw new InvalidDataException("Cannot infer game directory from texture PAK path.");
        var gameDirectory = Directory.GetParent(pakDirectory)?.FullName
            ?? throw new InvalidDataException("Cannot infer game directory from texture PAK path.");
        var worldDirectory = Path.Combine(gameDirectory, "World");
        if (!Directory.Exists(worldDirectory))
            throw new DirectoryNotFoundException($"World directory not found at expected path: {worldDirectory}");

        var floorPakArchive = FloorPakArchive.Load(Path.Combine(worldDirectory, "Floor.pak"));
        var staticPakArchive = StaticPakArchive.Load(Path.Combine(worldDirectory, "Static.pak"));
        return new SacredWorldArchive(
            Path.Combine(worldDirectory, "sectors.keyx"),
            File.OpenRead(Path.Combine(worldDirectory, "sectors.wldx")),
            floorPakArchive,
            staticPakArchive);
    }

    public async Task<Sector?> TryLoadSector(SectorCoord coord)
    {
        if (!_sectorIdByGrid.TryGetValue(coord, out var sectorId))
            return null;

        var entry = _entriesById[sectorId];

        var decompressed = await _wldxLoader.ReadWldx(entry, sectorId);

        await _sectorLoadLock.WaitAsync();
        var ground = new TileLayer(SectorW, SectorH);
        var floorOverlays = new FloorOverlayLayer(SectorW, SectorH);
        var liquidSurfaces = new LiquidSurfaceLayer();
        var staticObjects = new StaticObjectLayer();
        var staticTileVisits = new List<StaticTileVisit>();
        var tiles = decompressed.AsSpan(entry.TilesRelativeOffset, SectorW * SectorH * WldxTileRecord.Size);
        for (var y = 0; y < SectorH; y++)
        for (var x = 0; x < SectorW; x++)
        {
            var tileOffset = (y * SectorW + x) * WldxTileRecord.Size;
            var tile = WldxTileRecord.FromBytes(tiles.Slice(tileOffset, WldxTileRecord.Size));
            ground[x, y] = tile.GroundTileId;
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
        _sectorLoadLock.Release();
        return new Sector(coord, ground, floorOverlays, liquidSurfaces, staticObjects);
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
                    record.Value.TileOrBlendRef,
                    record.Value.PrimaryTileId,
                    record.Value.SecondaryTileId,
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
            surfaceType,
            styleId,
            tile.LiquidAlphaLeft,
            tile.LiquidAlphaTop,
            tile.LiquidAlphaRight,
            tile.LiquidAlphaBottom));
    }

    private void LoadKeyx(string path)
    {
        var data = File.ReadAllBytes(path);
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
        _wldxLoader.Dispose();
    }
}
