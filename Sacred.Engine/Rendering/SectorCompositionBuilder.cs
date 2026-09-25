using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;
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
    private const float ObjectShiftX = 47.8f;
    private const float ObjectShiftY = -0.3f;

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
        var coverTiles = new List<TerrainCompositionTile>();
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
            // Sacred queues all floor overlays before renderWater. The liquid's
            // authored vertex alpha then reveals the terrain beneath it; the
            // WLDX low nibble is tile behavior, not an overlay insertion depth.
            baseTiles.Add(compositionTile);
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

        var embeddedSprites = new List<TerrainEmbeddedSprite>();
        var embeddableObjects = new List<StaticWorldObject>();
        for (var layerIndex = 0; layerIndex < 2; layerIndex++)
        {
            var objects = layerIndex == 0
                ? sector.StaticObjects.Objects
                : sector.WorldObjects.Objects;
            foreach (var staticObject in objects)
            {
                if (staticObject.IsExcludedFromNormalRender)
                    continue;

                var item = assets.GetItem(staticObject.TypeId);
                if (item is { ModelDesc.SectorEmbeddable: true })
                    embeddableObjects.Add(staticObject);
            }
        }

        embeddableObjects.Sort(CompareStaticWorldObjects);
        foreach (var staticObject in embeddableObjects)
        {
            var sprite = await assets.GetStaticWorldSpriteAsync(
                staticObject.TypeId,
                staticObject.SpriteParam2E,
                staticObject.SpriteParam2F,
                staticObject.OrientationOrFrame,
                staticObject.AnimationFrameDurationTicks,
                staticObject.AnimationFrameCount).ConfigureAwait(false);

            if (sprite is null)
                continue;

            var footX = staticObject.ProjectedX + ObjectShiftX;
            var footY = staticObject.ProjectedY + ObjectShiftY;
            var spriteIsoX = footX - sprite.AnchorX;
            var spriteIsoY = footY - sprite.AnchorY;

            if (Math.Abs(spriteIsoX) > 1048576 || Math.Abs(spriteIsoY) > 1048576)
                continue;

            var screenX = spriteIsoX - (sectorOriginIso.X + sectorBounds.X);
            var screenY = spriteIsoY - (sectorOriginIso.Y + sectorBounds.Y);

            embeddedSprites.Add(new TerrainEmbeddedSprite(
                sprite,
                staticObject.StaticId,
                screenX,
                screenY));
        }

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
            floorMissingTiles,
            embeddedSprites.ToArray());
    }

    private int EngineQueueIndex(StaticWorldObject staticObject)
    {
        var item = assets.GetItem(staticObject.TypeId);
        var graphicFlags = item?.ModelDesc.GraphicFlags ?? SacredItemGraphicFlags.None;
        var category = item?.ModelDesc.Category ?? SacredItemCategory.Unspecified;
        if (category == SacredItemCategory.Effect)
        {
            if (graphicFlags.HasFlag(SacredItemGraphicFlags.FrontLayer))
                return 4;
            return 3;
        }

        return graphicFlags.HasFlag(SacredItemGraphicFlags.FrontLayer) ? 4 : 3;
    }

    private int CompareStaticWorldObjects(StaticWorldObject left, StaticWorldObject right)
    {
        var queue = EngineQueueIndex(left).CompareTo(EngineQueueIndex(right));
        if (queue != 0)
            return queue;

        return WorldStaticDrawOrder.Compare(left, right);
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
        TerrainTileSurface Surface);
}
