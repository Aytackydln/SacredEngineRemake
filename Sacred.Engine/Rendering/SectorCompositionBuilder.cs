using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.World.Geometry;

namespace Sacred.Engine.Rendering;

/// <summary>
/// Resolves Sacred tile identifiers into compact GPU composition instructions.
/// Pixel rasterization and layer blending are intentionally left to the GPU.
/// </summary>
internal sealed class SectorCompositionBuilder(AssetManager assets)
{
    private const int SourceTileWidth = 100;
    private const int SourceTileHeight = 50;
    private const int RenderTileWidth = 96;
    private const int RenderTileHeight = 48;
    private const int IsoStepWidth = IsometricProjection.StepWidth;
    private const int IsoStepHeight = IsometricProjection.StepHeight;
    private const int SectorImageOriginX = -(Sector.TileCount - 1) * (IsoStepWidth / 2);
    private const int SectorImageOriginY = 0;
    private const int SectorImageWidth = (Sector.TileCount - 1) * IsoStepWidth + RenderTileWidth;
    private const int SectorImageHeight = (Sector.TileCount - 1) * IsoStepHeight + RenderTileHeight;

    private static readonly (int X, int Y)[] TilePositions =
    [
        (0, 0), (104, 0), (52, 25), (156, 25),
        (0, 50), (104, 50), (52, 75), (156, 75),
        (0, 100), (104, 100), (52, 125), (156, 125),
        (0, 150), (104, 150), (52, 175), (156, 175),
        (0, 200), (104, 200),
    ];

    private readonly Dictionary<uint, TerrainTileSource?> _tileSources = new();
    private readonly HashSet<FloorSourceKey> _floorSources = [];
    private readonly StairsDebugTileSourceFactory _stairsDebugTiles = new();
    private readonly BlockedAreaDebugTileSourceFactory _blockedAreaDebugTile = new();
    private readonly object _cacheLock = new();

    public int CachedTileCount
    {
        get
        {
            lock (_cacheLock)
                return _tileSources.Count;
        }
    }

    public int CachedFloorCount
    {
        get
        {
            lock (_cacheLock)
                return _floorSources.Count;
        }
    }

    public async Task<TerrainSectorComposition> BuildAsync(Sector sector)
    {
        var sectorOriginIso = IsometricProjection.WorldToIso(
            sector.Coord.X * Sector.TileCount,
            sector.Coord.Y * Sector.TileCount);
        var baseTiles = new List<TerrainCompositionTile>(Sector.TileCount * Sector.TileCount + sector.FloorOverlays.Count);
        var coverTiles = new List<TerrainCompositionTile>(sector.FloorOverlays.Count);
        var stairsDebugTiles = new List<TerrainCompositionTile>(sector.StairsCells.Count);
        var blockedAreaDebugTiles = new List<TerrainCompositionTile>();

        var groundCandidateTiles = Sector.TileCount * Sector.TileCount;
        var groundDrawnTiles = 0;
        var groundMissingTiles = 0;
        var floorCandidateTiles = sector.FloorOverlays.Count;
        var floorDrawnTiles = 0;
        var floorMissingTiles = 0;

        var liquidInsertionDepths = new byte[Sector.TileCount * Sector.TileCount];
        Array.Fill(liquidInsertionDepths, byte.MaxValue);
        foreach (var liquid in sector.LiquidSurfaces.Surfaces)
            liquidInsertionDepths[liquid.LocalY * Sector.TileCount + liquid.LocalX] = liquid.FloorInsertionDepth;

        var drawTiles = new List<DrawTile>(Sector.TileCount * Sector.TileCount);
        for (var localY = 0; localY < Sector.TileCount; localY++)
        for (var localX = 0; localX < Sector.TileCount; localX++)
        {
            var iso = IsometricProjection.WorldToIso(localX, localY);
            drawTiles.Add(new DrawTile(
                localX + localY,
                localY,
                (int)MathF.Round(iso.X - SectorImageOriginX),
                (int)MathF.Round(iso.Y - SectorImageOriginY),
                sector.Ground[localX, localY],
                0,
                0,
                false));
        }

        drawTiles.Sort(CompareDrawTiles);
        foreach (var item in drawTiles)
        {
            var source = await GetTileSourceAsync(item.PrimaryTileId).ConfigureAwait(false);
            if (source is null)
            {
                groundMissingTiles++;
                continue;
            }

            baseTiles.Add(new TerrainCompositionTile(item.ScreenX, item.ScreenY, source, null));
            groundDrawnTiles++;
        }

        drawTiles.Clear();
        for (var localY = 0; localY < Sector.TileCount; localY++)
        for (var localX = 0; localX < Sector.TileCount; localX++)
        {
            var iso = IsometricProjection.WorldToIso(localX, localY);
            foreach (var overlay in sector.FloorOverlays[localX, localY])
            {
                drawTiles.Add(new DrawTile(
                    localX + localY,
                    localY,
                    (int)MathF.Round(iso.X - SectorImageOriginX),
                    (int)MathF.Round(iso.Y - SectorImageOriginY),
                    overlay.PrimaryTileId,
                    overlay.SecondaryTileId,
                    overlay.ChainDepth,
                    overlay.ChainDepth >= liquidInsertionDepths[localY * Sector.TileCount + localX]));
            }
        }

        drawTiles.Sort(CompareDrawTiles);
        foreach (var item in drawTiles)
        {
            var primary = await GetTileSourceAsync(item.PrimaryTileId).ConfigureAwait(false);
            if (primary is null)
            {
                floorMissingTiles++;
                continue;
            }

            TerrainTileSource? secondary = null;
            if (item.SecondaryTileId != 0)
                secondary = await GetTileSourceAsync(item.SecondaryTileId).ConfigureAwait(false);

            lock (_cacheLock)
                _floorSources.Add(new FloorSourceKey(item.PrimaryTileId, item.SecondaryTileId));

            var compositionTile = new TerrainCompositionTile(item.ScreenX, item.ScreenY, primary, secondary);
            (item.AboveLiquid ? coverTiles : baseTiles).Add(compositionTile);
            floorDrawnTiles++;
        }

        foreach (var cell in sector.StairsCells.Cells)
        {
            var position = cell.Position;
            var localX = position.X - sector.Coord.X * Sector.TileCount;
            var localY = position.Y - sector.Coord.Y * Sector.TileCount;
            var iso = IsometricProjection.WorldToIso(localX, localY);
            var isAnchor = position == cell.Anchor;
            stairsDebugTiles.Add(new TerrainCompositionTile(
                (int)MathF.Round(iso.X - SectorImageOriginX),
                (int)MathF.Round(iso.Y - SectorImageOriginY),
                _stairsDebugTiles.Get(isAnchor),
                null));
        }

        for (var localY = 0; localY < Sector.TileCount; localY++)
        for (var localX = 0; localX < Sector.TileCount; localX++)
        {
            if (!sector.Pathing.IsBlocked(localX, localY))
                continue;

            var iso = IsometricProjection.WorldToIso(localX, localY);
            blockedAreaDebugTiles.Add(new TerrainCompositionTile(
                (int)MathF.Round(iso.X - SectorImageOriginX),
                (int)MathF.Round(iso.Y - SectorImageOriginY),
                _blockedAreaDebugTile.Source,
                null));
        }

        var stairsDebugBounds = CropDebugTiles(stairsDebugTiles);
        var blockedAreaDebugBounds = CropDebugTiles(blockedAreaDebugTiles);

        return new TerrainSectorComposition(
            sector.Coord,
            sectorOriginIso.X + SectorImageOriginX,
            sectorOriginIso.Y + SectorImageOriginY,
            SectorImageWidth,
            SectorImageHeight,
            sector.Coord.X + sector.Coord.Y,
            baseTiles.ToArray(),
            coverTiles.ToArray(),
            stairsDebugTiles.ToArray(),
            stairsDebugBounds.X,
            stairsDebugBounds.Y,
            stairsDebugBounds.Width,
            stairsDebugBounds.Height,
            blockedAreaDebugTiles.ToArray(),
            blockedAreaDebugBounds.X,
            blockedAreaDebugBounds.Y,
            blockedAreaDebugBounds.Width,
            blockedAreaDebugBounds.Height,
            groundCandidateTiles,
            groundDrawnTiles,
            groundMissingTiles,
            floorCandidateTiles,
            floorDrawnTiles,
            floorMissingTiles);
    }

    private static DebugLayerBounds CropDebugTiles(List<TerrainCompositionTile> tiles)
    {
        if (tiles.Count == 0)
            return new DebugLayerBounds(0, 0, 1, 1);

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        foreach (var tile in tiles)
        {
            minX = Math.Min(minX, tile.ScreenX);
            minY = Math.Min(minY, tile.ScreenY);
            maxX = Math.Max(maxX, tile.ScreenX + RenderTileWidth);
            maxY = Math.Max(maxY, tile.ScreenY + RenderTileHeight);
        }

        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            tiles[index] = tile with
            {
                ScreenX = tile.ScreenX - minX,
                ScreenY = tile.ScreenY - minY,
            };
        }

        return new DebugLayerBounds(minX, minY, maxX - minX, maxY - minY);
    }

    private async Task<TerrainTileSource?> GetTileSourceAsync(uint tileId)
    {
        lock (_cacheLock)
            if (_tileSources.TryGetValue(tileId, out var cached))
                return cached;

        var source = await LoadTileSourceAsync(tileId).ConfigureAwait(false);
        lock (_cacheLock)
        {
            if (_tileSources.TryGetValue(tileId, out var cached))
                return cached;

            _tileSources[tileId] = source;
            return source;
        }
    }

    private async Task<TerrainTileSource?> LoadTileSourceAsync(uint tileId)
    {
        var definition = assets.GetTileDefinition(tileId);
        if (definition is null || string.IsNullOrWhiteSpace(definition.Value.FileName))
            return null;

        TextureAsset sheet;
        try
        {
            sheet = await assets.LoadTextureAsync(definition.Value.FileName).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            return null;
        }

        var position = TilePositions[(int)(definition.Value.TileNumber % TilePositions.Length)];
        if (position.X + SourceTileWidth > sheet.Width || position.Y + SourceTileHeight > sheet.Height)
            return null;

        return new TerrainTileSource(sheet, position.X, position.Y);
    }

    private static int CompareDrawTiles(DrawTile left, DrawTile right)
    {
        var depth = left.Depth.CompareTo(right.Depth);
        if (depth != 0)
            return depth;

        var worldY = left.WorldY.CompareTo(right.WorldY);
        return worldY != 0 ? worldY : left.ChainDepth.CompareTo(right.ChainDepth);
    }

    private readonly record struct FloorSourceKey(uint PrimaryTileId, uint SecondaryTileId);

    private readonly record struct DebugLayerBounds(int X, int Y, int Width, int Height);

    private readonly record struct DrawTile(
        int Depth,
        int WorldY,
        int ScreenX,
        int ScreenY,
        uint PrimaryTileId,
        uint SecondaryTileId,
        int ChainDepth,
        bool AboveLiquid);
}

public sealed record TerrainTileSource(TextureAsset Texture, int SourceX, int SourceY);

public readonly record struct TerrainCompositionTile(
    int ScreenX,
    int ScreenY,
    TerrainTileSource Primary,
    TerrainTileSource? Secondary);

public sealed class TerrainSectorComposition
{
    private TerrainCompositionTile[] _baseTiles;
    private TerrainCompositionTile[] _coverTiles;
    private TerrainCompositionTile[] _stairsDebugTiles;
    private TerrainCompositionTile[] _blockedAreaDebugTiles;

    public TerrainSectorComposition(
        SectorCoord coord,
        float isoX,
        float isoY,
        int width,
        int height,
        int depth,
        TerrainCompositionTile[] baseTiles,
        TerrainCompositionTile[] coverTiles,
        TerrainCompositionTile[] stairsDebugTiles,
        int stairsDebugOffsetX,
        int stairsDebugOffsetY,
        int stairsDebugWidth,
        int stairsDebugHeight,
        TerrainCompositionTile[] blockedAreaDebugTiles,
        int blockedAreaDebugOffsetX,
        int blockedAreaDebugOffsetY,
        int blockedAreaDebugWidth,
        int blockedAreaDebugHeight,
        int groundCandidateTiles,
        int groundDrawnTiles,
        int groundMissingTiles,
        int floorCandidateTiles,
        int floorDrawnTiles,
        int floorMissingTiles)
    {
        Coord = coord;
        IsoX = isoX;
        IsoY = isoY;
        Width = width;
        Height = height;
        Depth = depth;
        _baseTiles = baseTiles;
        _coverTiles = coverTiles;
        _stairsDebugTiles = stairsDebugTiles;
        StairsDebugOffsetX = stairsDebugOffsetX;
        StairsDebugOffsetY = stairsDebugOffsetY;
        StairsDebugWidth = stairsDebugWidth;
        StairsDebugHeight = stairsDebugHeight;
        HasStairsDebugData = stairsDebugTiles.Length > 0;
        _blockedAreaDebugTiles = blockedAreaDebugTiles;
        BlockedAreaDebugOffsetX = blockedAreaDebugOffsetX;
        BlockedAreaDebugOffsetY = blockedAreaDebugOffsetY;
        BlockedAreaDebugWidth = blockedAreaDebugWidth;
        BlockedAreaDebugHeight = blockedAreaDebugHeight;
        HasBlockedAreaDebugData = blockedAreaDebugTiles.Length > 0;
        GroundCandidateTiles = groundCandidateTiles;
        GroundDrawnTiles = groundDrawnTiles;
        GroundMissingTiles = groundMissingTiles;
        FloorCandidateTiles = floorCandidateTiles;
        FloorDrawnTiles = floorDrawnTiles;
        FloorMissingTiles = floorMissingTiles;
    }

    public SectorCoord Coord { get; }
    public float IsoX { get; }
    public float IsoY { get; }
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public IReadOnlyList<TerrainCompositionTile> BaseTiles => _baseTiles;
    public IReadOnlyList<TerrainCompositionTile> CoverTiles => _coverTiles;
    public IReadOnlyList<TerrainCompositionTile> StairsDebugTiles => _stairsDebugTiles;
    public int StairsDebugOffsetX { get; }
    public int StairsDebugOffsetY { get; }
    public int StairsDebugWidth { get; }
    public int StairsDebugHeight { get; }
    public bool HasStairsDebugData { get; }
    public IReadOnlyList<TerrainCompositionTile> BlockedAreaDebugTiles => _blockedAreaDebugTiles;
    public int BlockedAreaDebugOffsetX { get; }
    public int BlockedAreaDebugOffsetY { get; }
    public int BlockedAreaDebugWidth { get; }
    public int BlockedAreaDebugHeight { get; }
    public bool HasBlockedAreaDebugData { get; }
    public int GroundCandidateTiles { get; }
    public int GroundDrawnTiles { get; }
    public int GroundMissingTiles { get; }
    public int FloorCandidateTiles { get; }
    public int FloorDrawnTiles { get; }
    public int FloorMissingTiles { get; }

    internal void ReleaseSourceTiles()
    {
        Interlocked.Exchange(ref _baseTiles, []);
        Interlocked.Exchange(ref _coverTiles, []);
        Interlocked.Exchange(ref _stairsDebugTiles, []);
        Interlocked.Exchange(ref _blockedAreaDebugTiles, []);
    }
}
