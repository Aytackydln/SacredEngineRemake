using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core;
using Sacred.Core.World;
using Sacred.Engine.Assets;

namespace Sacred.Engine.World;

public sealed class SacredWorldArchive : IDisposable
{
    private const int KeyxHeaderSize = 0x100;
    private const int KeyxEntrySize = 0x300;
    private const int SectorW = Sector.TileCount;
    private const int SectorH = Sector.TileCount;
    private const int TileDescriptorSize = 0x20;
    private const int KeyxAbsoluteRawXOffset = 0x3C;
    private const int KeyxAbsoluteRawYOffset = 0x40;
    private const int KeyxCompressedOffset = 0x0EC;
    private const int KeyxCompressedSize = 0x0F0;
    private const int KeyxTilesRelativeOffset = 0x0D4;
    private const int KeyxTilesSizeOffset = 0x0D8;
    private const int KeyxAbsoluteBias = 0x19;
    private const int KeyxStyle90Offset = 0x2E0;
    private const int KeyxStyleA0Offset = 0x2E1;
    private const int TileStaticChainHeadOffset = 0x04;
    private const int TileLiquidAlpha0Offset = 0x10;
    private const int TileLiquidAlpha1Offset = 0x11;
    private const int TileLiquidAlpha2Offset = 0x12;
    private const int TileLiquidAlpha3Offset = 0x13;
    private const int TileFloorChainHeadOffset = 0x0C;
    private const int TileSurfaceTypeOffset = 0x1F;
    private const byte LiquidSurfaceTypeMask = 0xF0;
    private const byte LiquidSurfaceType90 = 0x90;
    private const byte LiquidSurfaceTypeA0 = 0xA0;
    private const int FloorChainMaxDepth = 128;
    private const int StaticChainMaxDepth = 4096;
    private static readonly SectorCoord BellevueSector = new(52, 38);

    private readonly FloorPakArchive _floorPak;
    private readonly StaticPakArchive _staticPak;
    private readonly Dictionary<uint, KeyxSectorEntry> _entriesById = new();
    private readonly Dictionary<SectorCoord, uint> _sectorIdByGrid = new();

    private readonly SemaphoreSlim _wldxLock = new(1, 1);
    private readonly FileStream _wldxStream;

    public SectorCoord StartSector { get; private set; }

    private SacredWorldArchive(string keyxPath, FileStream wldxStream, FloorPakArchive floorPak, StaticPakArchive staticPak)
    {
        _wldxStream = wldxStream;
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

        return new SacredWorldArchive(
            Path.Combine(worldDirectory, "sectors.keyx"),
            File.OpenRead(Path.Combine(worldDirectory, "sectors.wldx")),
            FloorPakArchive.Load(Path.Combine(worldDirectory, "Floor.pak")),
            StaticPakArchive.Load(Path.Combine(worldDirectory, "Static.pak")));
    }

    public async Task<Sector?> TryLoadSector(SectorCoord coord)
    {
        if (!_sectorIdByGrid.TryGetValue(coord, out var sectorId))
            return null;

        var entry = _entriesById[sectorId];

        await _wldxLock.WaitAsync();
        _wldxStream.Position = entry.CompressedOffset;

        if (entry.CompressedSize > int.MaxValue)
            throw new InvalidDataException($"Sector {sectorId} compressed block is too large.");

        var compressed = new byte[(int)entry.CompressedSize];
        await _wldxStream.ReadExactlyAsync(compressed);
        _wldxLock.Release();
        
        byte[] decompressed;
        using (var compressedStream = new MemoryStream(compressed, writable: false))
        await using (var zlib = new ZLibStream(compressedStream, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            await zlib.CopyToAsync(output);
            decompressed = output.ToArray();
        }

        if (entry.TilesRelativeOffset < 0 ||
            entry.TilesSize < SectorW * SectorH * TileDescriptorSize ||
            entry.TilesRelativeOffset + entry.TilesSize > decompressed.Length)
            throw new InvalidDataException($"Sector {sectorId} has an invalid tile block.");

        var ground = new TileLayer(SectorW, SectorH);
        var floorOverlays = new FloorOverlayLayer(SectorW, SectorH);
        var liquidSurfaces = new LiquidSurfaceLayer();
        var staticObjects = new StaticObjectLayer();
        var staticTileVisits = new List<StaticTileVisit>();
        var tiles = decompressed.AsSpan(entry.TilesRelativeOffset, SectorW * SectorH * TileDescriptorSize);
        for (var y = 0; y < SectorH; y++)
        for (var x = 0; x < SectorW; x++)
        {
            var tileOffset = (y * SectorW + x) * TileDescriptorSize;
            ground[x, y] = BitConverter.ToUInt32(tiles.Slice(tileOffset, 4));
            var staticHead = BitConverter.ToUInt32(tiles.Slice(tileOffset + TileStaticChainHeadOffset, 4));
            if (staticHead != 0)
            {
                var worldX = coord.X * SectorW + x;
                var worldY = coord.Y * SectorH + y;
                staticTileVisits.Add(new StaticTileVisit(worldX + worldY, worldY, worldX, staticHead));
            }

            LoadFloorOverlayChain(floorOverlays, x, y, BitConverter.ToUInt32(tiles.Slice(tileOffset + TileFloorChainHeadOffset, 4)));
            LoadLiquidSurface(liquidSurfaces, x, y, tiles.Slice(tileOffset, TileDescriptorSize), entry);
        }

        LoadStaticObjectChains(staticObjects, staticTileVisits);
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
                    record.Value.StaticId,
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
        ReadOnlySpan<byte> tile,
        KeyxSectorEntry entry)
    {
        var surfaceType = (byte)(tile[TileSurfaceTypeOffset] & LiquidSurfaceTypeMask);
        if (surfaceType is not LiquidSurfaceType90 and not LiquidSurfaceTypeA0)
            return;

        var styleId = surfaceType == LiquidSurfaceType90 ? entry.Style90 : entry.StyleA0;
        liquidSurfaces.Add(new LiquidSurface(
            x,
            y,
            surfaceType,
            styleId,
            unchecked((sbyte)tile[TileLiquidAlpha0Offset]),
            unchecked((sbyte)tile[TileLiquidAlpha1Offset]),
            unchecked((sbyte)tile[TileLiquidAlpha2Offset]),
            unchecked((sbyte)tile[TileLiquidAlpha3Offset])));
    }

    private void LoadKeyx(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < KeyxHeaderSize)
            throw new InvalidDataException("sectors.keyx is too small to contain a header.");

        var count = ReadEntryCount(data);
        var rawEntries = new List<(uint Id, byte[] Raw)>(count);
        for (var i = 0; i < count; i++)
        {
            var offset = KeyxHeaderSize + i * KeyxEntrySize;
            if (offset + KeyxEntrySize > data.Length)
                break;

            var raw = data[offset..(offset + KeyxEntrySize)];
            rawEntries.Add((BitConverter.ToUInt32(raw, 0x24), raw));
        }

        var scale = InferKeyxPositionScale(rawEntries);
        foreach (var (id, raw) in rawEntries)
        {
            var rawX = BitConverter.ToInt32(raw, KeyxAbsoluteRawXOffset);
            var rawY = BitConverter.ToInt32(raw, KeyxAbsoluteRawYOffset);
            var originX = RoundToSectorOrigin((rawX + KeyxAbsoluteBias) * scale);
            var originY = RoundToSectorOrigin((rawY + KeyxAbsoluteBias) * scale);
            var coord = new SectorCoord(originX / SectorW, originY / SectorH);

            var entry = new KeyxSectorEntry(
                id,
                coord,
                BitConverter.ToUInt32(raw, KeyxCompressedOffset),
                BitConverter.ToUInt32(raw, KeyxCompressedSize),
                checked((int)BitConverter.ToUInt32(raw, KeyxTilesRelativeOffset)),
                checked((int)BitConverter.ToUInt32(raw, KeyxTilesSizeOffset)),
                raw[KeyxStyle90Offset],
                raw[KeyxStyleA0Offset]);

            _entriesById[id] = entry;
            _sectorIdByGrid[coord] = id;
        }
    }

    private static int ReadEntryCount(byte[] data)
    {
        var count32 = BitConverter.ToUInt32(data, 4);
        var count16 = BitConverter.ToUInt16(data, 4);
        var maxCount = Math.Max(0, (data.Length - KeyxHeaderSize) / KeyxEntrySize);
        if (count32 <= maxCount)
            return (int)count32;
        if (count16 <= maxCount)
            return count16;

        throw new InvalidDataException($"Cannot determine sectors.keyx entry count. count16={count16}, count32={count32}, max={maxCount}");
    }

    private static float InferKeyxPositionScale(List<(uint Id, byte[] Raw)> entries)
    {
        var diffs = new List<int>();
        CollectDiffs(entries, KeyxAbsoluteRawXOffset, diffs);
        CollectDiffs(entries, KeyxAbsoluteRawYOffset, diffs);
        if (diffs.Count == 0)
            throw new InvalidDataException("Cannot infer KEYX absolute-position scale from sector positions.");

        var min = diffs[0];
        foreach (var diff in diffs)
            if (diff < min)
                min = diff;

        return SectorW / (float)min;
    }

    private static void CollectDiffs(List<(uint Id, byte[] Raw)> entries, int offset, List<int> diffs)
    {
        var values = new SortedSet<int>();
        foreach (var (_, raw) in entries)
            values.Add(BitConverter.ToInt32(raw, offset));

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
        foreach (var entry in _entriesById.Values)
            return entry.Grid;

        return new SectorCoord(3200, 2600);
    }

    private readonly record struct KeyxSectorEntry(
        uint Id,
        SectorCoord Grid,
        uint CompressedOffset,
        uint CompressedSize,
        int TilesRelativeOffset,
        int TilesSize,
        byte Style90,
        byte StyleA0);

    private readonly record struct StaticTileVisit(int Depth, int WorldY, int WorldX, uint StaticHeadId);

    public void Dispose()
    {
        _wldxStream.Dispose();
    }
}
