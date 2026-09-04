using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Lighting;
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
    private readonly TerrainTopologyDebugTileSourceFactory _terrainTopologyDebugTiles = new();
    private readonly object _cacheLock = new();
    private int _cachedTileCount;
    private int _cachedFloorCount;

    public int CachedTileCount => Volatile.Read(ref _cachedTileCount);

    public int CachedFloorCount => Volatile.Read(ref _cachedFloorCount);

    public async Task<TerrainSectorComposition> BuildAsync(Sector sector)
    {
        var sectorOriginIso = IsometricProjection.WorldToIso(
            sector.Coord.X * Sector.TileCount,
            sector.Coord.Y * Sector.TileCount);
        var sectorBounds = TerrainTileGeometry.CalculateSectorBounds(sector);
        var baseTiles = new List<TerrainCompositionTile>(Sector.TileCount * Sector.TileCount + sector.FloorOverlays.Count);
        var coverTiles = new List<TerrainCompositionTile>(sector.FloorOverlays.Count);
        var stairsDebugTiles = new List<TerrainCompositionTile>(sector.StairsCells.Count);
        var debugDoorTiles = new HashSet<(int X, int Y)>();
        var blockedAreaDebugTiles = new List<TerrainCompositionTile>();
        var terrainTopologyDebugTiles = new List<TerrainCompositionTile>(Sector.TileCount * Sector.TileCount);

        var groundCandidateTiles = Sector.TileCount * Sector.TileCount;
        var groundDrawnTiles = 0;
        var groundMissingTiles = 0;
        var floorCandidateTiles = 0;
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
            var surface = TerrainTileGeometry.GetSurface(sector, localX, localY);
            drawTiles.Add(new DrawTile(
                localX + localY,
                localY,
                (int)MathF.Round(iso.X - sectorBounds.X),
                (int)MathF.Round(iso.Y - sectorBounds.Y),
                sector.Ground[localX, localY],
                0,
                0,
                false,
                surface));
            terrainTopologyDebugTiles.Add(new TerrainCompositionTile(
                (int)MathF.Round(iso.X - sectorBounds.X),
                (int)MathF.Round(iso.Y - sectorBounds.Y),
                _terrainTopologyDebugTiles.Source,
                null,
                surface with { BakedLight = TerrainBakedLightTile.FullyLit }));
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

            var compositionTile = new TerrainCompositionTile(item.ScreenX, item.ScreenY, source, null, item.Surface);
            baseTiles.Add(compositionTile);
            groundDrawnTiles++;
        }

        drawTiles.Clear();
        for (var localY = 0; localY < Sector.TileCount; localY++)
        for (var localX = 0; localX < Sector.TileCount; localX++)
        {
            var iso = IsometricProjection.WorldToIso(localX, localY);
            var surface = TerrainTileGeometry.GetSurface(sector, localX, localY);
            foreach (var overlay in sector.FloorOverlays[localX, localY])
            {
                floorCandidateTiles++;
                var drawTile = new DrawTile(
                    localX + localY,
                    localY,
                    (int)MathF.Round(iso.X - sectorBounds.X),
                    (int)MathF.Round(iso.Y - sectorBounds.Y),
                    overlay.PrimaryTileId,
                    overlay.SecondaryTileId,
                    overlay.ChainDepth,
                    overlay.ChainDepth >= liquidInsertionDepths[localY * Sector.TileCount + localX],
                    surface);
                drawTiles.Add(drawTile);
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
            {
                _floorSources.Add(new FloorSourceKey(item.PrimaryTileId, item.SecondaryTileId));
                Volatile.Write(ref _cachedFloorCount, _floorSources.Count);
            }

            var compositionTile = new TerrainCompositionTile(item.ScreenX, item.ScreenY, primary, secondary, item.Surface);
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
                (int)MathF.Round(iso.X - sectorBounds.X),
                (int)MathF.Round(iso.Y - sectorBounds.Y),
                _stairsDebugTiles.Get(isAnchor),
                null,
                TerrainTileGeometry.GetSurface(sector, localX, localY)));
        }

        foreach (var group in sector.IndoorTileGroups.Groups)
        foreach (var entrance in group.Entrances)
        {
            if (entrance.WorldX < sector.Coord.X * Sector.TileCount ||
                entrance.WorldX >= (sector.Coord.X + 1) * Sector.TileCount ||
                entrance.WorldY < sector.Coord.Y * Sector.TileCount ||
                entrance.WorldY >= (sector.Coord.Y + 1) * Sector.TileCount)
            {
                continue;
            }

            if (!debugDoorTiles.Add((entrance.WorldX, entrance.WorldY)))
                continue;

            var localX = entrance.WorldX - sector.Coord.X * Sector.TileCount;
            var localY = entrance.WorldY - sector.Coord.Y * Sector.TileCount;
            var iso = IsometricProjection.WorldToIso(localX, localY);
            stairsDebugTiles.Add(new TerrainCompositionTile(
                (int)MathF.Round(iso.X - sectorBounds.X),
                (int)MathF.Round(iso.Y - sectorBounds.Y),
                _stairsDebugTiles.Get(isAnchor: true),
                null,
                TerrainTileGeometry.GetSurface(sector, localX, localY)));
        }

        for (var localY = 0; localY < Sector.TileCount; localY++)
        for (var localX = 0; localX < Sector.TileCount; localX++)
        {
            if (!sector.Pathing.IsBlocked(localX, localY))
                continue;

            var iso = IsometricProjection.WorldToIso(localX, localY);
            blockedAreaDebugTiles.Add(new TerrainCompositionTile(
                (int)MathF.Round(iso.X - sectorBounds.X),
                (int)MathF.Round(iso.Y - sectorBounds.Y),
                _blockedAreaDebugTile.Source,
                null,
                TerrainTileGeometry.GetSurface(sector, localX, localY)));
        }

        var stairsDebugBounds = TerrainTileGeometry.CropTiles(stairsDebugTiles);
        var blockedAreaDebugBounds = TerrainTileGeometry.CropTiles(blockedAreaDebugTiles);
        var terrainTopologyDebugBounds = TerrainTileGeometry.CropTiles(terrainTopologyDebugTiles);
        return new TerrainSectorComposition(
            sector.Coord,
            sectorOriginIso.X + sectorBounds.X,
            sectorOriginIso.Y + sectorBounds.Y,
            sectorBounds.Width,
            sectorBounds.Height,
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
            terrainTopologyDebugTiles.ToArray(),
            terrainTopologyDebugBounds.X,
            terrainTopologyDebugBounds.Y,
            terrainTopologyDebugBounds.Width,
            terrainTopologyDebugBounds.Height,
            groundCandidateTiles,
            groundDrawnTiles,
            groundMissingTiles,
            floorCandidateTiles,
            floorDrawnTiles,
            floorMissingTiles);
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
            Volatile.Write(ref _cachedTileCount, _tileSources.Count);
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
            sheet = await assets.LoadTerrainTextureAsync(definition.Value.FileName).ConfigureAwait(false);
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

    private readonly record struct DrawTile(
        int Depth,
        int WorldY,
        int ScreenX,
        int ScreenY,
        uint PrimaryTileId,
        uint SecondaryTileId,
        int ChainDepth,
        bool AboveLiquid,
        TerrainTileSurface Surface);
}
