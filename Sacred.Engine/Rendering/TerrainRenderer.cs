using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Assets;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World;
using Sacred.Engine.Assets;
using Sacred.Engine.Scene;

namespace Sacred.Engine.Rendering;

public sealed class TerrainRenderer
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
    private const int MaxConcurrentSectorImageBuilds = 1;
    private const uint StaticNormalRenderExcludeFlags = 0x290;
    private const int StaticExteriorActiveLayer = 1;
    private const byte StaticSpecialRenderClass = 0x0C;
    private const uint StaticRearGraphicFlag = 0x00000004;
    private const uint StaticFrontGraphicFlag = 0x00800000;
    private const float StaticObjectShiftX = 47.8f;
    private const float StaticObjectShiftY = -0.3f;
    private const int LiquidRenderTileWidth = 96;
    private const int LiquidRenderTileHeight = 48;
    private const int LiquidProjectedOffsetX = 2;
    private const int LiquidProjectedOffsetY = 1;

    private static readonly Vector2 SourceLeft = new(2.512f, 24.012f);
    private static readonly Vector2 SourceTop = new(50.512f, 1.012f);
    private static readonly Vector2 SourceBottom = new(50.000f, 48.512f);
    private static readonly Vector2 SourceRight = new(98.012f, 23.500f);
    private static readonly Vector2 SourceCenter = (SourceLeft + SourceTop + SourceBottom + SourceRight) * 0.25f;

    private static readonly Vector2 DestLeft = new(0.0f, RenderTileHeight * 0.5f);
    private static readonly Vector2 DestTop = new(RenderTileWidth * 0.5f, 0.0f);
    private static readonly Vector2 DestBottom = new(RenderTileWidth * 0.5f, RenderTileHeight - 1.0f);
    private static readonly Vector2 DestRight = new(RenderTileWidth - 1.0f, RenderTileHeight * 0.5f);
    private static readonly Vector2 DestCenter = new(RenderTileWidth * 0.5f, RenderTileHeight * 0.5f);

    private static readonly (int X, int Y)[] TilePositions =
    [
        (0, 0), (104, 0), (52, 25), (156, 25),
        (0, 50), (104, 50), (52, 75), (156, 75),
        (0, 100), (104, 100), (52, 125), (156, 125),
        (0, 150), (104, 150), (52, 175), (156, 175),
        (0, 200), (104, 200),
    ];

    private readonly AssetManager _assets;
    private readonly Dictionary<uint, TileImage?> _tileCache = new();
    private readonly Dictionary<uint, TileImage?> _floorCache = new();
    private readonly Dictionary<LiquidImageKey, TileImage?> _liquidCache = new();
    private readonly Dictionary<SectorCoord, TerrainSectorImage> _sectorCache = new();
    private readonly Dictionary<SectorCoord, Task<TerrainSectorImage>> _sectorBuildTasks = new();
    private readonly List<TerrainSectorImage> _visibleSectorImages = new(9);
    private readonly List<TerrainStaticSprite> _visibleStaticSprites = new(1024);
    private readonly List<Sector> _candidateSectors = new(9);
    private readonly HashSet<SectorCoord> _neededSectorCoords = new();
    private readonly List<SectorCoord> _sectorCoordsToRemove = new(9);
    private readonly List<SectorCoord> _completedSectorBuilds = new(9);
    private readonly object _tileCacheLock = new();

    public TerrainRenderStats LastStats { get; private set; }

    public TerrainRenderer(AssetManager assets)
    {
        _assets = assets;
    }

    public IReadOnlyList<TerrainSectorImage> PrepareVisibleWorld(VisibleWorld world)
    {
        SelectCandidateSectors(world);
        PruneSectorCache();
        PumpCompletedSectorBuilds();
        _visibleSectorImages.Clear();

        var candidateTiles = 0;
        var drawnTiles = 0;
        var missingTiles = 0;
        var floorCandidateTiles = 0;
        var drawnFloorTiles = 0;
        var missingFloorTiles = 0;
        var liquidCandidateTiles = 0;
        var drawnLiquidTiles = 0;
        var missingLiquidTiles = 0;

        foreach (var sector in _candidateSectors)
        {
            var image = GetSectorImageOrQueueBuild(sector);
            if (image is null)
                continue;

            candidateTiles += image.GroundCandidateTiles;
            drawnTiles += image.GroundDrawnTiles;
            missingTiles += image.GroundMissingTiles;
            floorCandidateTiles += image.FloorCandidateTiles;
            drawnFloorTiles += image.FloorDrawnTiles;
            missingFloorTiles += image.FloorMissingTiles;
            liquidCandidateTiles += image.LiquidCandidateTiles;
            drawnLiquidTiles += image.LiquidDrawnTiles;
            missingLiquidTiles += image.LiquidMissingTiles;
            _visibleSectorImages.Add(image);
        }

        _visibleSectorImages.Sort(static (left, right) =>
        {
            var depth = left.Depth.CompareTo(right.Depth);
            return depth != 0 ? depth : left.Coord.Y.CompareTo(right.Coord.Y);
        });

        LastStats = new TerrainRenderStats(
            _candidateSectors.Count,
            candidateTiles,
            drawnTiles,
            missingTiles,
            _tileCache.Count,
            floorCandidateTiles,
            drawnFloorTiles,
            missingFloorTiles,
            _floorCache.Count,
            liquidCandidateTiles,
            drawnLiquidTiles,
            missingLiquidTiles,
            _liquidCache.Count,
            0,
            0,
            0,
            _visibleSectorImages.Count,
            _sectorCache.Count,
            CountPendingSectorBuilds());

        return _visibleSectorImages;
    }

    public IReadOnlyList<TerrainStaticSprite> PrepareVisibleStaticSprites()
    {
        _visibleStaticSprites.Clear();

        var staticCandidateObjects = 0;
        var staticMissingObjects = 0;

        foreach (var sector in _candidateSectors)
        {
            staticCandidateObjects += sector.StaticObjects.Count;

            foreach (var staticObject in sector.StaticObjects.Objects)
            {
                if ((staticObject.Flags & StaticNormalRenderExcludeFlags) != 0)
                    continue;

                if (staticObject.SurfaceRenderLayer > StaticExteriorActiveLayer)
                    continue;

                if (!_assets.TryGetStaticSpriteOrRequest(staticObject.TypeId, out var sprite))
                {
                    staticMissingObjects++;
                    continue;
                }

                if (sprite is null)
                {
                    staticMissingObjects++;
                    continue;
                }

                var footX = staticObject.ProjectedX + StaticObjectShiftX;
                var footY = staticObject.ProjectedY + StaticObjectShiftY;
                var spriteIsoX = footX - sprite.AnchorX;
                var spriteIsoY = footY - sprite.AnchorY;

                if (Math.Abs(spriteIsoX) > 1048576 || Math.Abs(spriteIsoY) > 1048576)
                {
                    staticMissingObjects++;
                    continue;
                }

                _visibleStaticSprites.Add(new TerrainStaticSprite(
                    sprite,
                    spriteIsoX,
                    spriteIsoY,
                    footX,
                    footY,
                    staticObject.SurfaceRenderLayer,
                    StaticEngineQueueIndex(staticObject),
                    staticObject.TileDepth,
                    staticObject.TileWorldY,
                    staticObject.TileWorldX,
                    staticObject.ChainDepth,
                    staticObject.InsertionOrder));
            }
        }

        _visibleStaticSprites.Sort(CompareStaticSprites);
        LastStats = LastStats with
        {
            StaticCandidateObjects = staticCandidateObjects,
            StaticDrawnObjects = _visibleStaticSprites.Count,
            StaticMissingObjects = staticMissingObjects
        };

        return _visibleStaticSprites;
    }

    private void SelectCandidateSectors(VisibleWorld world)
    {
        _neededSectorCoords.Clear();
        _candidateSectors.Clear();

        foreach (var sector in world.Sectors)
        {
            _neededSectorCoords.Add(sector.Coord);
            _candidateSectors.Add(sector);
        }
    }

    private void PruneSectorCache()
    {
        _sectorCoordsToRemove.Clear();
        foreach (var coord in _sectorCache.Keys)
            if (!_neededSectorCoords.Contains(coord))
                _sectorCoordsToRemove.Add(coord);

        foreach (var coord in _sectorCoordsToRemove)
            _sectorCache.Remove(coord);
    }

    private void PumpCompletedSectorBuilds()
    {
        _completedSectorBuilds.Clear();
        foreach (var (coord, task) in _sectorBuildTasks)
            if (task.IsCompleted)
                _completedSectorBuilds.Add(coord);

        foreach (var coord in _completedSectorBuilds)
        {
            var task = _sectorBuildTasks[coord];
            _sectorBuildTasks.Remove(coord);
            if (task.Status == TaskStatus.RanToCompletion && _neededSectorCoords.Contains(coord))
                _sectorCache[coord] = task.Result;
        }
    }

    private TerrainSectorImage? GetSectorImageOrQueueBuild(Sector sector)
    {
        if (_sectorCache.TryGetValue(sector.Coord, out var cached))
            return cached;

        if (!_sectorBuildTasks.ContainsKey(sector.Coord) && CountPendingSectorBuilds() < MaxConcurrentSectorImageBuilds)
            _sectorBuildTasks[sector.Coord] = Task.Run(() => BuildSectorImageAsync(sector));

        return null;
    }

    private int CountPendingSectorBuilds()
    {
        var count = 0;
        foreach (var task in _sectorBuildTasks.Values)
            if (!task.IsCompleted)
                count++;

        return count;
    }

    private async Task<TerrainSectorImage> BuildSectorImageAsync(Sector sector)
    {
        var sectorOriginIso = WorldToIso(
            sector.Coord.X * Sector.TileCount,
            sector.Coord.Y * Sector.TileCount);

        var groundCandidateTiles = Sector.TileCount * Sector.TileCount;
        var groundDrawnTiles = 0;
        var groundMissingTiles = 0;
        var floorCandidateTiles = sector.FloorOverlays.Count;
        var floorDrawnTiles = 0;
        var floorMissingTiles = 0;
        var liquidCandidateTiles = sector.LiquidSurfaces.Count;
        var liquidDrawnTiles = 0;
        var liquidMissingTiles = 0;
        var imageMinX = SectorImageOriginX;
        var imageMinY = SectorImageOriginY;
        var imageMaxX = SectorImageOriginX + SectorImageWidth;
        var imageMaxY = SectorImageOriginY + SectorImageHeight;

        var imageWidth = Math.Max(1, imageMaxX - imageMinX);
        var imageHeight = Math.Max(1, imageMaxY - imageMinY);
        var rgba = new byte[imageWidth * imageHeight * 4];

        var composeTiles = new List<DrawTile>(Sector.TileCount * Sector.TileCount);
        for (var ly = 0; ly < Sector.TileCount; ly++)
        for (var lx = 0; lx < Sector.TileCount; lx++)
        {
            var iso = WorldToIso(lx, ly);
            composeTiles.Add(new DrawTile(
                lx + ly,
                ly,
                (int)MathF.Round(iso.X - imageMinX),
                (int)MathF.Round(iso.Y - imageMinY),
                RenderTileWidth,
                RenderTileHeight,
                sector.Ground[lx, ly],
                0,
                0));
        }

        composeTiles.Sort(CompareDrawTiles);
        foreach (var item in composeTiles)
        {
            var tile = await GetTileImageAsync(item.TileId);
            if (tile is null)
            {
                groundMissingTiles++;
                continue;
            }

            DrawUnscaledRgba(rgba, imageWidth, imageHeight, tile.Rgba, tile.Width, tile.Height, item.ScreenX, item.ScreenY);
            groundDrawnTiles++;
        }

        composeTiles.Clear();
        for (var ly = 0; ly < Sector.TileCount; ly++)
        for (var lx = 0; lx < Sector.TileCount; lx++)
        {
            var iso = WorldToIso(lx, ly);
            foreach (var overlay in sector.FloorOverlays[lx, ly])
            {
                composeTiles.Add(new DrawTile(
                    lx + ly,
                    ly,
                    (int)MathF.Round(iso.X - imageMinX),
                    (int)MathF.Round(iso.Y - imageMinY),
                    RenderTileWidth,
                    RenderTileHeight,
                    overlay.PrimaryTileId,
                    overlay.SecondaryTileId,
                    overlay.ChainDepth));
            }
        }

        composeTiles.Sort(CompareDrawTiles);
        foreach (var item in composeTiles)
        {
            var tile = await GetFloorImageAsync(item.TileId, item.SecondaryTileId);
            if (tile is null)
            {
                floorMissingTiles++;
                continue;
            }

            DrawUnscaledRgba(rgba, imageWidth, imageHeight, tile.Rgba, tile.Width, tile.Height, item.ScreenX, item.ScreenY);
            floorDrawnTiles++;
        }

        foreach (var liquid in sector.LiquidSurfaces.Surfaces)
        {
            var textureName = LiquidTextureName(liquid);
            var liquidTile = await GetLiquidImageAsync(textureName, LiquidCornerAlphas(liquid));
            if (liquidTile is null)
            {
                liquidMissingTiles++;
                continue;
            }

            var iso = WorldToIso(liquid.LocalX, liquid.LocalY);
            var x = (int)MathF.Round(iso.X + LiquidProjectedOffsetX - imageMinX);
            var y = (int)MathF.Round(iso.Y + LiquidProjectedOffsetY - imageMinY);
            DrawUnscaledRgba(rgba, imageWidth, imageHeight, liquidTile.Rgba, liquidTile.Width, liquidTile.Height, x, y);
            liquidDrawnTiles++;
        }

        return new TerrainSectorImage(
            sector.Coord,
            sectorOriginIso.X + imageMinX,
            sectorOriginIso.Y + imageMinY,
            imageWidth,
            imageHeight,
            rgba,
            sector.Coord.X + sector.Coord.Y,
            groundCandidateTiles,
            groundDrawnTiles,
            groundMissingTiles,
            floorCandidateTiles,
            floorDrawnTiles,
            floorMissingTiles,
            liquidCandidateTiles,
            liquidDrawnTiles,
            liquidMissingTiles,
            0,
            0,
            0);
    }

    private static int CompareDrawTiles(DrawTile left, DrawTile right)
    {
        var depth = left.Depth.CompareTo(right.Depth);
        if (depth != 0)
            return depth;

        var worldY = left.WorldY.CompareTo(right.WorldY);
        if (worldY != 0)
            return worldY;

        return left.ChainDepth.CompareTo(right.ChainDepth);
    }

    private static int CompareStaticSprites(TerrainStaticSprite left, TerrainStaticSprite right)
    {
        var queue = left.QueueIndex.CompareTo(right.QueueIndex);
        if (queue != 0)
            return queue;

        var tileDepth = left.TileDepth.CompareTo(right.TileDepth);
        if (tileDepth != 0)
            return tileDepth;

        var tileWorldY = left.TileWorldY.CompareTo(right.TileWorldY);
        if (tileWorldY != 0)
            return tileWorldY;

        var tileWorldX = left.TileWorldX.CompareTo(right.TileWorldX);
        if (tileWorldX != 0)
            return tileWorldX;

        var chainDepth = left.ChainDepth.CompareTo(right.ChainDepth);
        if (chainDepth != 0)
            return chainDepth;

        return left.InsertionOrder.CompareTo(right.InsertionOrder);
    }

    private int StaticEngineQueueIndex(StaticWorldObject staticObject)
    {
        var item = _assets.GetItem(staticObject.TypeId);
        var graphicFlags = item?.GraphicRenderFlags ?? 0;
        var renderClass = item?.RenderClass ?? 0;

        if (renderClass == StaticSpecialRenderClass)
        {
            if ((graphicFlags & StaticFrontGraphicFlag) != 0)
                return 4;
            if ((graphicFlags & StaticRearGraphicFlag) != 0)
                return 0;
            return 3;
        }

        if ((graphicFlags & StaticRearGraphicFlag) != 0)
            return (staticObject.Flags & 0x20) != 0 || staticObject.SurfaceRenderLayer == 1 ? 0 : 2;

        if ((graphicFlags & StaticFrontGraphicFlag) != 0)
            return 4;

        return 3;
    }

    private async Task<TileImage?> GetTileImageAsync(uint tileId)
    {
        lock (_tileCacheLock)
            if (_tileCache.TryGetValue(tileId, out var cached))
                return cached;

        var image = await BuildTileImageAsync(tileId, forceOpaque: false);
        lock (_tileCacheLock)
        {
            if (_tileCache.TryGetValue(tileId, out var cached))
                return cached;

            return _tileCache[tileId] = image;
        }
    }

    private async Task<TileImage?> GetFloorImageAsync(uint primaryTileId, uint secondaryTileId)
    {
        var packedRef = primaryTileId | (secondaryTileId << 17);
        lock (_tileCacheLock)
            if (_floorCache.TryGetValue(packedRef, out var cached))
                return cached;

        var primary = await BuildTileImageAsync(primaryTileId, forceOpaque: false);
        if (primary is null)
            return CacheFloorImage(packedRef, null);

        if (secondaryTileId == 0)
            return CacheFloorImage(packedRef, primary);

        var mask = await BuildTileImageAsync(secondaryTileId, forceOpaque: false);
        if (mask is null)
            return CacheFloorImage(packedRef, primary);

        var rgba = new byte[primary.Rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i + 0] = primary.Rgba[i + 0];
            rgba[i + 1] = primary.Rgba[i + 1];
            rgba[i + 2] = primary.Rgba[i + 2];
            rgba[i + 3] = mask.Rgba[i + 3];
        }

        return CacheFloorImage(packedRef, new TileImage(primary.Width, primary.Height, rgba));
    }

    private TileImage? CacheFloorImage(uint packedRef, TileImage? image)
    {
        lock (_tileCacheLock)
        {
            if (_floorCache.TryGetValue(packedRef, out var cached))
                return cached;

            return _floorCache[packedRef] = image;
        }
    }

    private async Task<TileImage?> GetLiquidImageAsync(string textureName, LiquidAlphas alphas)
    {
        var key = new LiquidImageKey(textureName, alphas);
        lock (_tileCacheLock)
            if (_liquidCache.TryGetValue(key, out var cached))
                return cached;

        TextureAsset texture;
        try
        {
            texture = await _assets.LoadTextureAsync(textureName);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            return CacheLiquidImage(key, null);
        }

        return CacheLiquidImage(
            key,
            new TileImage(
                LiquidRenderTileWidth,
                LiquidRenderTileHeight,
                BuildLiquidDiamond(texture, alphas)));
    }

    private TileImage? CacheLiquidImage(LiquidImageKey key, TileImage? image)
    {
        lock (_tileCacheLock)
        {
            if (_liquidCache.TryGetValue(key, out var cached))
                return cached;

            return _liquidCache[key] = image;
        }
    }

    private static string LiquidTextureName(LiquidSurface surface)
    {
        var style = LiquidStyle.For(surface.StyleId);
        var suffix = style.TextureKind switch
        {
            LiquidTextureKind.Lava => "LAVA",
            LiquidTextureKind.Schwefel => "SCHWEFEL",
            _ => "WATER"
        };

        return $"{style.Family}_{suffix}02.TGA";
    }

    private static LiquidAlphas LiquidCornerAlphas(LiquidSurface surface)
    {
        var multiplier = LiquidStyle.For(surface.StyleId).MainAlphaMultiplier;
        return new LiquidAlphas(
            LiquidAlpha(surface.AlphaLeft, multiplier),
            LiquidAlpha(surface.AlphaTop, multiplier),
            LiquidAlpha(surface.AlphaRight, multiplier),
            LiquidAlpha(surface.AlphaBottom, multiplier));
    }

    private static byte LiquidAlpha(sbyte value, int multiplier) =>
        (byte)Math.Clamp(value * multiplier, 0, 255);

    private static byte[] BuildLiquidDiamond(TextureAsset texture, LiquidAlphas alphas)
    {
        var rgba = new byte[LiquidRenderTileWidth * LiquidRenderTileHeight * 4];
        var halfW = LiquidRenderTileWidth * 0.5f;
        var halfH = LiquidRenderTileHeight * 0.5f;
        var centerAlpha = (alphas.Left + alphas.Top + alphas.Right + alphas.Bottom) * 0.25f;
        var center = new Vector2(halfW, halfH);
        var top = new Vector2(halfW, 0.0f);
        var right = new Vector2(LiquidRenderTileWidth, halfH);
        var bottom = new Vector2(halfW, LiquidRenderTileHeight);
        var left = new Vector2(0.0f, halfH);

        for (var y = 0; y < LiquidRenderTileHeight; y++)
        for (var x = 0; x < LiquidRenderTileWidth; x++)
        {
            var nx = MathF.Abs((x + 0.5f - halfW) / halfW);
            var ny = MathF.Abs((y + 0.5f - halfH) / halfH);
            if (nx + ny > 1.0f)
                continue;

            var sx = Math.Clamp(x * texture.Width / LiquidRenderTileWidth, 0, texture.Width - 1);
            var sy = Math.Clamp(y * texture.Height / LiquidRenderTileHeight, 0, texture.Height - 1);
            var sourceOffset = (sy * texture.Width + sx) * 4;
            var destOffset = (y * LiquidRenderTileWidth + x) * 4;
            var sourceAlpha = texture.Rgba8[sourceOffset + 3];
            var point = new Vector2(x + 0.5f, y + 0.5f);
            var vertexAlpha = point.Y < halfH
                ? point.X < halfW
                    ? InterpolateTriangleAlpha(point, center, left, top, centerAlpha, alphas.Left, alphas.Top)
                    : InterpolateTriangleAlpha(point, center, top, right, centerAlpha, alphas.Top, alphas.Right)
                : point.X < halfW
                    ? InterpolateTriangleAlpha(point, center, bottom, left, centerAlpha, alphas.Bottom, alphas.Left)
                    : InterpolateTriangleAlpha(point, center, right, bottom, centerAlpha, alphas.Right, alphas.Bottom);

            rgba[destOffset + 0] = texture.Rgba8[sourceOffset + 0];
            rgba[destOffset + 1] = texture.Rgba8[sourceOffset + 1];
            rgba[destOffset + 2] = texture.Rgba8[sourceOffset + 2];
            rgba[destOffset + 3] = (byte)(sourceAlpha * vertexAlpha / 255);
        }

        return rgba;
    }

    private static int InterpolateTriangleAlpha(
        Vector2 point,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        float alphaA,
        float alphaB,
        float alphaC)
    {
        var denom = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (MathF.Abs(denom) < 0.0001f)
            return (int)MathF.Round(alphaA);

        var wA = ((b.Y - c.Y) * (point.X - c.X) + (c.X - b.X) * (point.Y - c.Y)) / denom;
        var wB = ((c.Y - a.Y) * (point.X - c.X) + (a.X - c.X) * (point.Y - c.Y)) / denom;
        var wC = 1.0f - wA - wB;
        return Math.Clamp((int)MathF.Round(alphaA * wA + alphaB * wB + alphaC * wC), 0, 255);
    }

    private async Task<TileImage?> BuildTileImageAsync(uint tileId, bool forceOpaque)
    {
        var definition = _assets.GetTileDefinition(tileId);
        if (definition is null || string.IsNullOrWhiteSpace(definition.Value.FileName))
            return null;

        TextureAsset sheet;
        try
        {
            sheet = await _assets.LoadTextureAsync(definition.Value.FileName);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            return null;
        }

        var position = TilePositions[(int)(definition.Value.TileNumber % TilePositions.Length)];
        if (position.X + SourceTileWidth > sheet.Width || position.Y + SourceTileHeight > sheet.Height)
            return null;

        return new TileImage(RenderTileWidth, RenderTileHeight, BuildDiamondTile(sheet, position, forceOpaque));
    }

    private static byte[] BuildDiamondTile(TextureAsset sheet, (int X, int Y) sheetOrigin, bool forceOpaque)
    {
        var rgba = new byte[RenderTileWidth * RenderTileHeight * 4];
        var halfW = RenderTileWidth * 0.5f;
        var halfH = RenderTileHeight * 0.5f;

        for (var y = 0; y < RenderTileHeight; y++)
        for (var x = 0; x < RenderTileWidth; x++)
        {
            var nx = MathF.Abs((x + 0.5f - halfW) / halfW);
            var ny = MathF.Abs((y + 0.5f - halfH) / halfH);
            if (nx + ny > 1.0f)
                continue;

            var sx = sheetOrigin.X + Math.Clamp(x * SourceTileWidth / RenderTileWidth, 0, SourceTileWidth - 1);
            var sy = sheetOrigin.Y + Math.Clamp(y * SourceTileHeight / RenderTileHeight, 0, SourceTileHeight - 1);
            var sourceOffset = (sy * sheet.Width + sx) * 4;
            var destOffset = (y * RenderTileWidth + x) * 4;

            rgba[destOffset + 0] = sheet.Rgba8[sourceOffset + 0];
            rgba[destOffset + 1] = sheet.Rgba8[sourceOffset + 1];
            rgba[destOffset + 2] = sheet.Rgba8[sourceOffset + 2];
            rgba[destOffset + 3] = forceOpaque ? (byte)255 : sheet.Rgba8[sourceOffset + 3];
        }

        return rgba;
    }

    private static void RasterizeTriangle(
        byte[] dest,
        TextureAsset sheet,
        (int X, int Y) sheetOrigin,
        Vector2 d0,
        Vector2 d1,
        Vector2 d2,
        Vector2 s0,
        Vector2 s1,
        Vector2 s2)
    {
        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(d0.X, MathF.Min(d1.X, d2.X))));
        var maxX = Math.Min(RenderTileWidth - 1, (int)MathF.Ceiling(MathF.Max(d0.X, MathF.Max(d1.X, d2.X))));
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(d0.Y, MathF.Min(d1.Y, d2.Y))));
        var maxY = Math.Min(RenderTileHeight - 1, (int)MathF.Ceiling(MathF.Max(d0.Y, MathF.Max(d1.Y, d2.Y))));

        var denom = (d1.Y - d2.Y) * (d0.X - d2.X) + (d2.X - d1.X) * (d0.Y - d2.Y);
        if (MathF.Abs(denom) < 0.0001f)
            return;

        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var px = x + 0.5f;
            var py = y + 0.5f;
            var w0 = ((d1.Y - d2.Y) * (px - d2.X) + (d2.X - d1.X) * (py - d2.Y)) / denom;
            var w1 = ((d2.Y - d0.Y) * (px - d2.X) + (d0.X - d2.X) * (py - d2.Y)) / denom;
            var w2 = 1.0f - w0 - w1;

            if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f)
                continue;

            var sx = sheetOrigin.X + (int)MathF.Round(s0.X * w0 + s1.X * w1 + s2.X * w2);
            var sy = sheetOrigin.Y + (int)MathF.Round(s0.Y * w0 + s1.Y * w1 + s2.Y * w2);
            sx = Math.Clamp(sx, 0, sheet.Width - 1);
            sy = Math.Clamp(sy, 0, sheet.Height - 1);

            var sourceOffset = (sy * sheet.Width + sx) * 4;
            var destOffset = (y * RenderTileWidth + x) * 4;
            dest[destOffset + 0] = sheet.Rgba8[sourceOffset + 0];
            dest[destOffset + 1] = sheet.Rgba8[sourceOffset + 1];
            dest[destOffset + 2] = sheet.Rgba8[sourceOffset + 2];
            dest[destOffset + 3] = sheet.Rgba8[sourceOffset + 3];
        }
    }

    private static Vector2 WorldToIso(float worldX, float worldY) =>
        IsometricProjection.WorldToIso(worldX, worldY);

    private static void DrawUnscaledRgba(
        byte[] dest,
        int destWidth,
        int destHeight,
        byte[] sourceRgba,
        int sourceWidth,
        int sourceHeight,
        int x,
        int y)
    {
        var startX = Math.Max(0, x);
        var startY = Math.Max(0, y);
        var endX = Math.Min(destWidth, x + sourceWidth);
        var endY = Math.Min(destHeight, y + sourceHeight);
        if (startX >= endX || startY >= endY)
            return;

        for (var dy = startY; dy < endY; dy++)
        {
            var sourceY = dy - y;
            for (var dx = startX; dx < endX; dx++)
            {
                var sourceX = dx - x;
                var si = (sourceY * sourceWidth + sourceX) * 4;
                var alpha = sourceRgba[si + 3];
                if (alpha == 0)
                    continue;

                var di = (dy * destWidth + dx) * 4;
                if (alpha == 255 || dest[di + 3] == 0)
                {
                    dest[di + 0] = sourceRgba[si + 0];
                    dest[di + 1] = sourceRgba[si + 1];
                    dest[di + 2] = sourceRgba[si + 2];
                    dest[di + 3] = alpha;
                    continue;
                }

                var destAlpha = dest[di + 3];
                var inverse = 255 - alpha;
                var outAlpha = alpha + destAlpha * inverse / 255;
                if (outAlpha == 0)
                    continue;

                var destFactor = destAlpha * inverse / 255;
                dest[di + 0] = (byte)((sourceRgba[si + 0] * alpha + dest[di + 0] * destFactor) / outAlpha);
                dest[di + 1] = (byte)((sourceRgba[si + 1] * alpha + dest[di + 1] * destFactor) / outAlpha);
                dest[di + 2] = (byte)((sourceRgba[si + 2] * alpha + dest[di + 2] * destFactor) / outAlpha);
                dest[di + 3] = (byte)outAlpha;
            }
        }
    }

    private sealed record TileImage(int Width, int Height, byte[] Rgba);

    private readonly record struct LiquidImageKey(string TextureName, LiquidAlphas Alphas);

    private readonly record struct LiquidAlphas(byte Left, byte Top, byte Right, byte Bottom);

    private enum LiquidTextureKind
    {
        Water,
        Lava,
        Schwefel
    }

    private readonly record struct LiquidStyle(LiquidTextureKind TextureKind, string Family, int MainAlphaMultiplier)
    {
        public static LiquidStyle For(byte styleId) => styleId switch
        {
            0 => new LiquidStyle(LiquidTextureKind.Water, "B", -12),
            1 => new LiquidStyle(LiquidTextureKind.Water, "B", -12),
            2 => new LiquidStyle(LiquidTextureKind.Water, "C", -12),
            3 => new LiquidStyle(LiquidTextureKind.Water, "D", -12),
            4 => new LiquidStyle(LiquidTextureKind.Lava, "A", -255),
            5 => new LiquidStyle(LiquidTextureKind.Lava, "B", -255),
            6 => new LiquidStyle(LiquidTextureKind.Lava, "C", -255),
            7 => new LiquidStyle(LiquidTextureKind.Schwefel, "A", -255),
            8 => new LiquidStyle(LiquidTextureKind.Lava, "D", -255),
            9 => new LiquidStyle(LiquidTextureKind.Water, "E", -255),
            10 => new LiquidStyle(LiquidTextureKind.Water, "F", -24),
            11 => new LiquidStyle(LiquidTextureKind.Water, "G", -12),
            12 => new LiquidStyle(LiquidTextureKind.Lava, "E", -255),
            13 => new LiquidStyle(LiquidTextureKind.Water, "B", -12),
            _ => new LiquidStyle(LiquidTextureKind.Water, "C", -12)
        };
    }

    private readonly record struct DrawTile(
        int Depth,
        int WorldY,
        int ScreenX,
        int ScreenY,
        int Width,
        int Height,
        uint TileId,
        uint SecondaryTileId,
        int ChainDepth);
}

public sealed class TerrainSectorImage
{
    public TerrainSectorImage(
        SectorCoord coord,
        float isoX,
        float isoY,
        int width,
        int height,
        byte[] rgba,
        int depth,
        int groundCandidateTiles,
        int groundDrawnTiles,
        int groundMissingTiles,
        int floorCandidateTiles,
        int floorDrawnTiles,
        int floorMissingTiles,
        int liquidCandidateTiles,
        int liquidDrawnTiles,
        int liquidMissingTiles,
        int staticCandidateObjects,
        int staticDrawnObjects,
        int staticMissingObjects)
    {
        Coord = coord;
        IsoX = isoX;
        IsoY = isoY;
        Width = width;
        Height = height;
        Rgba = rgba;
        Depth = depth;
        GroundCandidateTiles = groundCandidateTiles;
        GroundDrawnTiles = groundDrawnTiles;
        GroundMissingTiles = groundMissingTiles;
        FloorCandidateTiles = floorCandidateTiles;
        FloorDrawnTiles = floorDrawnTiles;
        FloorMissingTiles = floorMissingTiles;
        LiquidCandidateTiles = liquidCandidateTiles;
        LiquidDrawnTiles = liquidDrawnTiles;
        LiquidMissingTiles = liquidMissingTiles;
        StaticCandidateObjects = staticCandidateObjects;
        StaticDrawnObjects = staticDrawnObjects;
        StaticMissingObjects = staticMissingObjects;
    }

    public SectorCoord Coord { get; }
    public float IsoX { get; }
    public float IsoY { get; }
    public int Width { get; }
    public int Height { get; }
    public byte[]? Rgba { get; private set; }
    public int Depth { get; }
    public int GroundCandidateTiles { get; }
    public int GroundDrawnTiles { get; }
    public int GroundMissingTiles { get; }
    public int FloorCandidateTiles { get; }
    public int FloorDrawnTiles { get; }
    public int FloorMissingTiles { get; }
    public int LiquidCandidateTiles { get; }
    public int LiquidDrawnTiles { get; }
    public int LiquidMissingTiles { get; }
    public int StaticCandidateObjects { get; }
    public int StaticDrawnObjects { get; }
    public int StaticMissingObjects { get; }

    public bool HasCpuPixels => Rgba is not null;

    public byte[] GetCpuPixels()
    {
        return Rgba ?? throw new InvalidOperationException($"CPU pixels for sector {Coord.X},{Coord.Y} have already been released.");
    }

    public int ReleaseCpuPixels()
    {
        var rgba = Rgba;
        Rgba = null;
        return rgba?.Length ?? 0;
    }
}

public sealed record TerrainStaticSprite(
    StaticSpriteAsset Sprite,
    float IsoX,
    float IsoY,
    float DepthX,
    float DepthY,
    short SurfaceRenderLayer,
    int QueueIndex,
    int TileDepth,
    int TileWorldY,
    int TileWorldX,
    int ChainDepth,
    int InsertionOrder);

public readonly record struct TerrainRenderStats(
    int VisibleSectors,
    int CandidateTiles,
    int DrawnTiles,
    int MissingTiles,
    int CachedTiles,
    int FloorCandidateTiles,
    int FloorDrawnTiles,
    int FloorMissingTiles,
    int FloorCachedTiles,
    int LiquidCandidateTiles,
    int LiquidDrawnTiles,
    int LiquidMissingTiles,
    int LiquidCachedTiles,
    int StaticCandidateObjects,
    int StaticDrawnObjects,
    int StaticMissingObjects,
    int SectorImagesDrawn,
    int SectorImagesCached,
    int SectorImagesPending);
