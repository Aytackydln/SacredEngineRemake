using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Assets.Paks.Tiles;
using Sacred.Core.Pak.Items;
using Sacred.Core.World;
using Sacred.Core.World.Sector;
using Sacred.World.Geometry;

namespace Sacred.World.Rendering;

/// <summary>Builds a deterministic daytime terrain view without a graphics device.</summary>
public sealed class DayWorldRasterizer(
    SacredWorldArchive world,
    TexturePakArchive textures,
    TilesPakArchive tiles,
    WorldStaticSpriteProvider? staticSprites = null)
{
    private const int SourceTileWidth = 100;
    private const int SourceTileHeight = 50;
    private const int RenderTileWidth = 96;
    private const int RenderTileHeight = 48;
    private const int ExteriorActiveLayer = 1;
    private const float ObjectShiftX = 47.8f;
    private const float ObjectShiftY = -0.3f;

    private static readonly (int X, int Y)[] TilePositions =
    [
        (0, 0), (104, 0), (52, 25), (156, 25),
        (0, 50), (104, 50), (52, 75), (156, 75),
        (0, 100), (104, 100), (52, 125), (156, 125),
        (0, 150), (104, 150), (52, 175), (156, 175),
        (0, 200), (104, 200)
    ];

    private readonly Dictionary<uint, Task<TerrainTileSource?>> _tileSourceLoads = [];

    public async Task<DayWorldRenderResult> RenderAsync(
        Vector2 worldCenter,
        int width = 1280,
        int height = 720,
        float zoom = 0.75f,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!float.IsFinite(zoom) || zoom <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(zoom));

        var centerSector = new SectorCoord(
            (int)MathF.Floor(worldCenter.X / Sector.TileCount),
            (int)MathF.Floor(worldCenter.Y / Sector.TileCount));
        var sectorLoads = new List<Task<Sector?>>(9);
        for (var deltaY = -1; deltaY <= 1; deltaY++)
        for (var deltaX = -1; deltaX <= 1; deltaX++)
            sectorLoads.Add(world.TryLoadSector(new SectorCoord(centerSector.X + deltaX, centerSector.Y + deltaY)));

        var sectors = (await Task.WhenAll(sectorLoads).ConfigureAwait(false))
            .Where(static sector => sector is not null)
            .Select(static sector => sector!)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var centerIso = IsometricProjection.WorldToIso(worldCenter) + IsometricProjection.TileAnchorOffset;
        var draws = BuildDraws(sectors, centerIso, width, height, zoom);
        var tileIds = draws
            .SelectMany(static draw => draw.SecondaryTileId == 0
                ? new[] { draw.PrimaryTileId }
                : new[] { draw.PrimaryTileId, draw.SecondaryTileId })
            .Distinct()
            .ToArray();
        await Task.WhenAll(tileIds.Select(GetTileSourceAsync)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var canvas = new RgbaCanvas(width, height, 12, 15, 17);
        var rendered = 0;
        var missing = 0;
        foreach (var draw in draws)
        {
            var primary = await GetTileSourceAsync(draw.PrimaryTileId).ConfigureAwait(false);
            if (primary is null)
            {
                missing++;
                continue;
            }

            TerrainTileSource? secondary = null;
            if (draw.SecondaryTileId != 0)
                secondary = await GetTileSourceAsync(draw.SecondaryTileId).ConfigureAwait(false);
            canvas.DrawTerrainDiamond(
                primary.Texture,
                primary.SourceX,
                primary.SourceY,
                secondary?.Texture,
                secondary?.SourceX ?? 0,
                secondary?.SourceY ?? 0,
                draw.ScreenX,
                draw.ScreenY,
                RenderTileWidth * zoom,
                RenderTileHeight * zoom);
            rendered++;
        }

        var liquidResult = await new WorldLiquidRasterizer(textures)
            .RenderAsync(canvas, sectors, centerIso, width, height, zoom)
            .ConfigureAwait(false);
        foreach (var cover in liquidResult.Covers)
        {
            var primary = await GetTileSourceAsync(cover.PrimaryTileId).ConfigureAwait(false);
            if (primary is null)
                continue;
            var secondary = cover.SecondaryTileId == 0
                ? null
                : await GetTileSourceAsync(cover.SecondaryTileId).ConfigureAwait(false);
            canvas.DrawTerrainDiamond(
                primary.Texture,
                primary.SourceX,
                primary.SourceY,
                secondary?.Texture,
                secondary?.SourceX ?? 0,
                secondary?.SourceY ?? 0,
                cover.ScreenX,
                cover.ScreenY,
                RenderTileWidth * zoom,
                RenderTileHeight * zoom);
        }
        cancellationToken.ThrowIfCancellationRequested();

        var staticResult = staticSprites is null
            ? default
            : await DrawStaticSpritesAsync(canvas, sectors, centerIso, width, height, zoom, cancellationToken)
                .ConfigureAwait(false);
        canvas.DrawCross(width / 2, height / 2, 6, 255, 224, 128);
        return new DayWorldRenderResult(
            canvas.ToImage(),
            sectors.Length,
            draws.Count,
            rendered,
            missing,
            staticResult.Candidates,
            staticResult.Rendered,
            staticResult.Missing,
            liquidResult.Candidates,
            liquidResult.Rendered);
    }

    private async Task<StaticRenderResult> DrawStaticSpritesAsync(
        RgbaCanvas canvas,
        IReadOnlyList<Sector> sectors,
        Vector2 centerIso,
        int width,
        int height,
        float zoom,
        CancellationToken cancellationToken)
    {
        var draws = new List<StaticDraw>();
        var candidates = 0;
        foreach (var sector in sectors)
        foreach (var staticObject in sector.StaticObjects.Objects)
        {
            candidates++;
            if (staticObject.IsExcludedFromNormalRender ||
                staticObject.SurfaceRenderLayer > ExteriorActiveLayer)
                continue;
            var item = staticSprites!.GetItem(staticObject.TypeId);
            if (item is null ||
                staticObject.Flags.HasFlag(StaticObjectFlags.NightOnly) &&
                item.Value.StaticSpriteFrameCount <= 1)
                continue;

            var footX = staticObject.ProjectedX + ObjectShiftX;
            var footY = staticObject.ProjectedY + ObjectShiftY;
            var screenFootX = width * 0.5f + (footX - centerIso.X) * zoom;
            var screenFootY = height * 0.5f + (footY - centerIso.Y) * zoom;
            if (screenFootX < -512 || screenFootX > width + 512 || screenFootY < -512 || screenFootY > height + 512)
                continue;
            draws.Add(new StaticDraw(
                EngineQueueIndex(item.Value.GraphicFlags, item.Value.Category),
                staticObject,
                footX,
                footY,
                staticSprites.LoadAsync(staticObject)));
        }

        draws.Sort(CompareStaticDraws);
        await Task.WhenAll(draws.Select(static draw => draw.SpriteLoad)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var rendered = 0;
        var missing = 0;
        foreach (var draw in draws)
        {
            var sprite = await draw.SpriteLoad.ConfigureAwait(false);
            if (sprite is null)
            {
                missing++;
                continue;
            }
            var left = width * 0.5f + (draw.FootX - sprite.AnchorX - centerIso.X) * zoom;
            var top = height * 0.5f + (draw.FootY - sprite.AnchorY - centerIso.Y) * zoom;
            canvas.DrawTexture(
                new TextureAsset($"static-{sprite.GroupId}", sprite.Width, sprite.Height, sprite.Rgba),
                left,
                top,
                sprite.Width * zoom,
                sprite.Height * zoom);
            rendered++;
        }
        return new StaticRenderResult(candidates, rendered, missing);
    }

    private static int EngineQueueIndex(SacredItemGraphicFlags graphicFlags, SacredItemCategory category)
    {
        if (category == SacredItemCategory.Effect)
        {
            if (graphicFlags.HasFlag(SacredItemGraphicFlags.FrontLayer))
                return 4;
            return 3;
        }
        return graphicFlags.HasFlag(SacredItemGraphicFlags.FrontLayer) ? 4 : 3;
    }

    private static int CompareStaticDraws(StaticDraw left, StaticDraw right)
    {
        var queue = left.QueueIndex.CompareTo(right.QueueIndex);
        if (queue != 0)
            return queue;
        var tileDepth = left.Object.TileDepth.CompareTo(right.Object.TileDepth);
        if (tileDepth != 0)
            return tileDepth;
        var tileWorldY = left.Object.TileWorldY.CompareTo(right.Object.TileWorldY);
        if (tileWorldY != 0)
            return tileWorldY;
        var tileWorldX = left.Object.TileWorldX.CompareTo(right.Object.TileWorldX);
        if (tileWorldX != 0)
            return tileWorldX;
        var chainDepth = left.Object.ChainDepth.CompareTo(right.Object.ChainDepth);
        return chainDepth != 0 ? chainDepth : left.Object.InsertionOrder.CompareTo(right.Object.InsertionOrder);
    }

    private static List<WorldTileDraw> BuildDraws(
        IReadOnlyList<Sector> sectors,
        Vector2 centerIso,
        int width,
        int height,
        float zoom)
    {
        var ground = new List<WorldTileDraw>(sectors.Count * Sector.TileCount * Sector.TileCount);
        var floors = new List<WorldTileDraw>();
        foreach (var sector in sectors)
        {
            var sectorOriginX = sector.Coord.X * Sector.TileCount;
            var sectorOriginY = sector.Coord.Y * Sector.TileCount;
            for (var localY = 0; localY < Sector.TileCount; localY++)
            for (var localX = 0; localX < Sector.TileCount; localX++)
            {
                var worldX = sectorOriginX + localX;
                var worldY = sectorOriginY + localY;
                var iso = IsometricProjection.WorldToIso(worldX, worldY);
                var screenX = width * 0.5f + (iso.X - centerIso.X) * zoom;
                var screenY = height * 0.5f + (iso.Y - centerIso.Y) * zoom;
                if (screenX >= width || screenY >= height ||
                    screenX + RenderTileWidth * zoom <= 0 || screenY + RenderTileHeight * zoom <= 0)
                {
                    continue;
                }

                ground.Add(new WorldTileDraw(
                    worldX + worldY,
                    worldY,
                    0,
                    screenX,
                    screenY,
                    sector.Ground[localX, localY],
                    0));
                foreach (var floor in sector.FloorOverlays[localX, localY])
                {
                    floors.Add(new WorldTileDraw(
                        worldX + worldY,
                        worldY,
                        floor.ChainDepth,
                        screenX,
                        screenY,
                        floor.PrimaryTileId,
                        floor.SecondaryTileId));
                }
            }
        }

        ground.Sort(CompareDraws);
        floors.Sort(CompareDraws);
        ground.AddRange(floors);
        return ground;
    }

    private static int CompareDraws(WorldTileDraw left, WorldTileDraw right)
    {
        var depth = left.Depth.CompareTo(right.Depth);
        if (depth != 0)
            return depth;
        var worldY = left.WorldY.CompareTo(right.WorldY);
        return worldY != 0 ? worldY : left.ChainDepth.CompareTo(right.ChainDepth);
    }

    private Task<TerrainTileSource?> GetTileSourceAsync(uint tileId)
    {
        lock (_tileSourceLoads)
        {
            if (_tileSourceLoads.TryGetValue(tileId, out var load))
                return load;
            load = LoadTileSourceAsync(tileId);
            _tileSourceLoads.Add(tileId, load);
            return load;
        }
    }

    private async Task<TerrainTileSource?> LoadTileSourceAsync(uint tileId)
    {
        var definition = tiles.Get(tileId);
        if (definition is null || string.IsNullOrWhiteSpace(definition.Value.FileName))
            return null;
        try
        {
            var texture = await textures.LoadTextureAsync(definition.Value.FileName).ConfigureAwait(false);
            var position = TilePositions[(int)(definition.Value.TileNumber % TilePositions.Length)];
            return position.X + SourceTileWidth <= texture.Width && position.Y + SourceTileHeight <= texture.Height
                ? new TerrainTileSource(texture, position.X, position.Y)
                : null;
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    private readonly record struct WorldTileDraw(
        int Depth,
        int WorldY,
        int ChainDepth,
        float ScreenX,
        float ScreenY,
        uint PrimaryTileId,
        uint SecondaryTileId);

    private sealed record TerrainTileSource(TextureAsset Texture, int SourceX, int SourceY);
    private readonly record struct StaticDraw(
        int QueueIndex,
        StaticWorldObject Object,
        float FootX,
        float FootY,
        Task<WorldStaticSprite?> SpriteLoad);
    private readonly record struct StaticRenderResult(int Candidates, int Rendered, int Missing);
}

public sealed record DayWorldRenderResult(
    RgbaImage Image,
    int LoadedSectors,
    int CandidateTiles,
    int RenderedTiles,
    int MissingTiles,
    int StaticCandidateObjects,
    int StaticRenderedObjects,
    int StaticMissingObjects,
    int LiquidCandidateTiles,
    int LiquidRenderedTiles);
