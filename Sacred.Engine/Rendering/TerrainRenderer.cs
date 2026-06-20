using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Sector;
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
    private const int LiquidRenderTileWidth = RenderTileWidth;
    private const int LiquidRenderTileHeight = RenderTileHeight;
    private const int LiquidProjectedOffsetX = 2;
    private const int LiquidProjectedOffsetY = 1;

    private static readonly Vector2 SourceLeft = new(2.512f, 24.012f);
    private static readonly Vector2 SourceTop = new(50.512f, 1.012f);
    private static readonly Vector2 SourceBottom = new(50.000f, 48.512f);
    private static readonly Vector2 SourceRight = new(98.012f, 23.500f);
    private static readonly Vector2 SourceCenter = (SourceLeft + SourceTop + SourceBottom + SourceRight) * 0.25f;

    private static readonly Vector2 DestLeft = new(0.0f, RenderTileHeight * 0.5f);
    private static readonly Vector2 DestTop = new(RenderTileWidth * 0.5f, 0.0f);
    // These vertices describe the outer texture boundary, rather than the
    // last texel index. Rasterization evaluates pixel centres, so ending at
    // 95/47 would leave the right/bottom edge texels uncovered.
    private static readonly Vector2 DestBottom = new(RenderTileWidth * 0.5f, RenderTileHeight);
    private static readonly Vector2 DestRight = new(RenderTileWidth, RenderTileHeight * 0.5f);
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
    private readonly Dictionary<SectorCoord, TerrainSectorImage> _sectorCache = new();
    private readonly Dictionary<SectorCoord, Task<TerrainSectorImage>> _sectorBuildTasks = new();
    private readonly List<TerrainSectorImage> _visibleSectorImages = new(9);
    private readonly List<TerrainLiquidSprite> _visibleLiquidSprites = new(4096);
    private readonly List<TerrainStaticSprite> _visibleStaticSprites = new(1024);
    private readonly List<Sector> _candidateSectors = new(9);
    private readonly HashSet<SectorCoord> _neededSectorCoords = new();
    private readonly List<SectorCoord> _sectorCoordsToRemove = new(9);
    private readonly List<SectorCoord> _completedSectorBuilds = new(9);
    private readonly TextureFrameSequenceAsset?[] _liquidAnimationsByStyle = new TextureFrameSequenceAsset?[256];
    private readonly byte[] _liquidAnimationStates = new byte[256];
    private readonly object _tileCacheLock = new();
    private VisibleWorld? _preparedWorld;
    private bool _worldChangedThisFrame;
    private bool _staticAssetRequestsPending = true;
    private bool _liquidAssetRequestsPending = true;

    public TerrainRenderStats LastStats { get; private set; }
    public ulong WorldSpriteRevision { get; private set; }

    public TerrainRenderer(AssetManager assets)
    {
        _assets = assets;
    }

    public IReadOnlyList<TerrainSectorImage> PrepareVisibleWorld(VisibleWorld world)
    {
        _worldChangedThisFrame = !ReferenceEquals(_preparedWorld, world);
        if (_worldChangedThisFrame)
        {
            _preparedWorld = world;
            SelectCandidateSectors(world);
            PruneSectorCache();
        }

        var sectorBuildCompleted = PumpCompletedSectorBuilds();
        if (!_worldChangedThisFrame && !sectorBuildCompleted)
            return _visibleSectorImages;

        _visibleSectorImages.Clear();

        var candidateTiles = 0;
        var drawnTiles = 0;
        var missingTiles = 0;
        var floorCandidateTiles = 0;
        var drawnFloorTiles = 0;
        var missingFloorTiles = 0;

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
            _visibleSectorImages.Add(image);
        }

        _visibleSectorImages.Sort(static (left, right) =>
        {
            var depth = left.Depth.CompareTo(right.Depth);
            return depth != 0 ? depth : left.Coord.Y.CompareTo(right.Coord.Y);
        });

        var previousStats = LastStats;
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
            previousStats.LiquidCandidateTiles,
            previousStats.LiquidDrawnTiles,
            previousStats.LiquidMissingTiles,
            previousStats.LiquidCachedTiles,
            previousStats.StaticCandidateObjects,
            previousStats.StaticDrawnObjects,
            previousStats.StaticMissingObjects,
            _visibleSectorImages.Count,
            _sectorCache.Count,
            CountPendingSectorBuilds());

        return _visibleSectorImages;
    }

    public IReadOnlyList<TerrainStaticSprite> PrepareVisibleStaticSprites()
    {
        if (!_worldChangedThisFrame && !_staticAssetRequestsPending)
            return _visibleStaticSprites;

        _visibleStaticSprites.Clear();

        var staticCandidateObjects = 0;
        var staticMissingObjects = 0;
        var requestsPending = false;

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
                    requestsPending = true;
                    staticMissingObjects++;
                    continue;
                }

                if (sprite is null)
                {
                    var miniObjectReady = _assets.TryGetMiniObjectSpriteOrRequest(
                        staticObject.TypeId,
                        staticObject.SpriteParam2E,
                        staticObject.SpriteParam2F,
                        staticObject.OrientationOrFrame,
                        out sprite);
                    if (!miniObjectReady)
                    {
                        requestsPending = true;
                        staticMissingObjects++;
                        continue;
                    }
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
        _staticAssetRequestsPending = requestsPending;
        WorldSpriteRevision++;
        LastStats = LastStats with
        {
            StaticCandidateObjects = staticCandidateObjects,
            StaticDrawnObjects = _visibleStaticSprites.Count,
            StaticMissingObjects = staticMissingObjects
        };

        return _visibleStaticSprites;
    }

    public IReadOnlyList<TerrainLiquidSprite> PrepareVisibleLiquidSprites()
    {
        if (!_worldChangedThisFrame && !_liquidAssetRequestsPending)
            return _visibleLiquidSprites;

        _visibleLiquidSprites.Clear();

        var liquidCandidateTiles = 0;
        var liquidMissingTiles = 0;
        var requestsPending = false;
        foreach (var sector in _candidateSectors)
        {
            liquidCandidateTiles += sector.LiquidSurfaces.Count;
            var sectorOriginIso = WorldToIso(
                sector.Coord.X * Sector.TileCount,
                sector.Coord.Y * Sector.TileCount);
            foreach (var liquid in sector.LiquidSurfaces.Surfaces)
            {
                var style = LiquidStyle.For(liquid.StyleId);
                if (!TryGetLiquidAnimation(liquid.StyleId, style, out var animation, out var requestPending))
                {
                    requestsPending |= requestPending;
                    liquidMissingTiles++;
                    continue;
                }

                var localIso = WorldToIso(liquid.LocalX, liquid.LocalY);
                var worldX = sector.Coord.X * Sector.TileCount + liquid.LocalX;
                var worldY = sector.Coord.Y * Sector.TileCount + liquid.LocalY;
                var alphas = LiquidCornerAlphas(liquid);
                _visibleLiquidSprites.Add(new TerrainLiquidSprite(
                    animation!,
                    sector.Coord,
                    sectorOriginIso.X + localIso.X + LiquidProjectedOffsetX,
                    sectorOriginIso.Y + localIso.Y + LiquidProjectedOffsetY,
                    LiquidRenderTileWidth,
                    LiquidRenderTileHeight,
                    alphas.Left,
                    alphas.Top,
                    alphas.Right,
                    alphas.Bottom,
                    (byte)((worldX & 3) | ((worldY & 3) << 2)),
                    LiquidStyle.AnimationPeriodSeconds));
            }
        }

        _visibleLiquidSprites.Sort(static (left, right) =>
        {
            var depth = (left.SectorCoord.X + left.SectorCoord.Y).CompareTo(
                right.SectorCoord.X + right.SectorCoord.Y);
            if (depth != 0)
                return depth;

            var sectorY = left.SectorCoord.Y.CompareTo(right.SectorCoord.Y);
            if (sectorY != 0)
                return sectorY;

            var y = left.IsoY.CompareTo(right.IsoY);
            return y != 0 ? y : left.IsoX.CompareTo(right.IsoX);
        });
        _liquidAssetRequestsPending = requestsPending;
        WorldSpriteRevision++;
        LastStats = LastStats with
        {
            LiquidCandidateTiles = liquidCandidateTiles,
            LiquidDrawnTiles = _visibleLiquidSprites.Count,
            LiquidMissingTiles = liquidMissingTiles
        };

        return _visibleLiquidSprites;
    }

    private bool TryGetLiquidAnimation(
        byte styleId,
        LiquidStyle style,
        out TextureFrameSequenceAsset? animation,
        out bool requestPending)
    {
        var state = _liquidAnimationStates[styleId];
        if (state == 1)
        {
            animation = _liquidAnimationsByStyle[styleId];
            requestPending = false;
            return true;
        }

        if (state == 2)
        {
            animation = null;
            requestPending = false;
            return false;
        }

        var ready = _assets.TryGetTextureFrameSequenceOrRequest(
            style.FrameNameFormat,
            style.FrameCount,
            out animation);
        if (!ready)
        {
            requestPending = true;
            return false;
        }

        requestPending = false;
        if (animation is null)
        {
            _liquidAnimationStates[styleId] = 2;
            return false;
        }

        _liquidAnimationsByStyle[styleId] = animation;
        _liquidAnimationStates[styleId] = 1;
        return true;
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

    private bool PumpCompletedSectorBuilds()
    {
        _completedSectorBuilds.Clear();
        foreach (var (coord, task) in _sectorBuildTasks)
            if (task.IsCompleted)
                _completedSectorBuilds.Add(coord);

        if (_completedSectorBuilds.Count == 0)
            return false;

        foreach (var coord in _completedSectorBuilds)
        {
            var task = _sectorBuildTasks[coord];
            _sectorBuildTasks.Remove(coord);
            if (task.Status == TaskStatus.RanToCompletion && _neededSectorCoords.Contains(coord))
                _sectorCache[coord] = task.Result;
        }

        return true;
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
        var imageMinX = SectorImageOriginX;
        var imageMinY = SectorImageOriginY;
        var imageMaxX = SectorImageOriginX + SectorImageWidth;
        var imageMaxY = SectorImageOriginY + SectorImageHeight;

        var imageWidth = Math.Max(1, imageMaxX - imageMinX);
        var imageHeight = Math.Max(1, imageMaxY - imageMinY);
        var rgba = new byte[imageWidth * imageHeight * 4];
        var liquidCoverRgba = new byte[rgba.Length];
        var liquidInsertionDepths = new byte[Sector.TileCount * Sector.TileCount];
        Array.Fill(liquidInsertionDepths, byte.MaxValue);
        foreach (var liquid in sector.LiquidSurfaces.Surfaces)
        {
            liquidInsertionDepths[liquid.LocalY * Sector.TileCount + liquid.LocalX] =
                liquid.FloorInsertionDepth;
        }

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
                0,
                false));
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
                    overlay.ChainDepth,
                    overlay.ChainDepth >= liquidInsertionDepths[ly * Sector.TileCount + lx]));
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

            var destination = item.AboveLiquid ? liquidCoverRgba : rgba;
            DrawUnscaledRgba(destination, imageWidth, imageHeight, tile.Rgba, tile.Width, tile.Height, item.ScreenX, item.ScreenY);
            floorDrawnTiles++;
        }

        PremultiplyAlpha(liquidCoverRgba);

        return new TerrainSectorImage(
            sector.Coord,
            sectorOriginIso.X + imageMinX,
            sectorOriginIso.Y + imageMinY,
            imageWidth,
            imageHeight,
            rgba,
            liquidCoverRgba,
            sector.Coord.X + sector.Coord.Y,
            groundCandidateTiles,
            groundDrawnTiles,
            groundMissingTiles,
            floorCandidateTiles,
            floorDrawnTiles,
            floorMissingTiles,
            0,
            0,
            0,
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

        var image = await BuildTileImageAsync(tileId);
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

        var primary = await BuildTileImageAsync(primaryTileId);
        if (primary is null)
            return CacheFloorImage(packedRef, null);

        if (secondaryTileId == 0)
            return CacheFloorImage(packedRef, primary);

        var mask = await BuildTileImageAsync(secondaryTileId);
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

    private async Task<TileImage?> BuildTileImageAsync(uint tileId)
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

        return new TileImage(RenderTileWidth, RenderTileHeight, BuildDiamondTile(sheet, position));
    }

    private static byte[] BuildDiamondTile(TextureAsset sheet, (int X, int Y) sheetOrigin)
    {
        var rgba = new byte[RenderTileWidth * RenderTileHeight * 4];

        // A tile occupies a diamond within its 100x50 atlas cell. Scaling the
        // complete rectangular cell samples the matte/adjacent-tile pixels
        // outside that diamond, which leak as dots along the diamond edges.
        RasterizeTriangle(rgba, sheet, sheetOrigin, DestCenter, DestLeft, DestTop, SourceCenter, SourceLeft, SourceTop);
        RasterizeTriangle(rgba, sheet, sheetOrigin, DestCenter, DestTop, DestRight, SourceCenter, SourceTop, SourceRight);
        RasterizeTriangle(rgba, sheet, sheetOrigin, DestCenter, DestRight, DestBottom, SourceCenter, SourceRight, SourceBottom);
        RasterizeTriangle(rgba, sheet, sheetOrigin, DestCenter, DestBottom, DestLeft, SourceCenter, SourceBottom, SourceLeft);

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

    private static void PremultiplyAlpha(byte[] rgba)
    {
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var alpha = rgba[i + 3];
            if (alpha == 255)
                continue;

            rgba[i + 0] = (byte)(rgba[i + 0] * alpha / 255);
            rgba[i + 1] = (byte)(rgba[i + 1] * alpha / 255);
            rgba[i + 2] = (byte)(rgba[i + 2] * alpha / 255);
        }
    }

    private sealed record TileImage(int Width, int Height, byte[] Rgba);

    private readonly record struct LiquidAlphas(byte Left, byte Top, byte Right, byte Bottom);

    private enum LiquidTextureKind
    {
        Water,
        Lava,
        Schwefel
    }

    private readonly record struct LiquidStyle(
        LiquidTextureKind TextureKind,
        string Family,
        int MainAlphaMultiplier,
        int FrameCount)
    {
        public const float AnimationPeriodSeconds = 2.048f;

        public string FrameNameFormat => $"{Family}_{TextureKindName}{{0:00}}.TGA";

        private string TextureKindName => TextureKind switch
        {
            LiquidTextureKind.Lava => "LAVA",
            LiquidTextureKind.Schwefel => "SCHWEFEL",
            _ => "WATER"
        };

        public static LiquidStyle For(byte styleId) => styleId switch
        {
            0 => new LiquidStyle(LiquidTextureKind.Water, "B", -12, 50),
            1 => new LiquidStyle(LiquidTextureKind.Water, "B", -12, 50),
            2 => new LiquidStyle(LiquidTextureKind.Water, "C", -12, 50),
            3 => new LiquidStyle(LiquidTextureKind.Water, "D", -12, 50),
            4 => new LiquidStyle(LiquidTextureKind.Lava, "A", -255, 50),
            5 => new LiquidStyle(LiquidTextureKind.Lava, "B", -255, 50),
            6 => new LiquidStyle(LiquidTextureKind.Lava, "C", -255, 20),
            7 => new LiquidStyle(LiquidTextureKind.Schwefel, "A", -255, 20),
            8 => new LiquidStyle(LiquidTextureKind.Lava, "D", -255, 50),
            9 => new LiquidStyle(LiquidTextureKind.Water, "E", -255, 50),
            10 => new LiquidStyle(LiquidTextureKind.Water, "F", -24, 50),
            11 => new LiquidStyle(LiquidTextureKind.Water, "G", -12, 50),
            12 => new LiquidStyle(LiquidTextureKind.Lava, "E", -255, 50),
            13 => new LiquidStyle(LiquidTextureKind.Water, "B", -12, 50),
            _ => new LiquidStyle(LiquidTextureKind.Water, "C", -12, 50)
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
        int ChainDepth,
        bool AboveLiquid);
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
        byte[] liquidCoverRgba,
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
        LiquidCoverRgba = liquidCoverRgba;
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
    public byte[]? LiquidCoverRgba { get; private set; }
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

    public bool HasCpuPixels => Rgba is not null && LiquidCoverRgba is not null;

    public byte[] GetCpuPixels()
    {
        return Rgba ?? throw new InvalidOperationException($"CPU pixels for sector {Coord.X},{Coord.Y} have already been released.");
    }

    public byte[] GetLiquidCoverCpuPixels()
    {
        return LiquidCoverRgba ?? throw new InvalidOperationException($"CPU liquid-cover pixels for sector {Coord.X},{Coord.Y} have already been released.");
    }

    public int ReleaseCpuPixels()
    {
        var rgba = Rgba;
        var liquidCoverRgba = LiquidCoverRgba;
        Rgba = null;
        LiquidCoverRgba = null;
        return (rgba?.Length ?? 0) + (liquidCoverRgba?.Length ?? 0);
    }
}

public readonly record struct TerrainStaticSprite(
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

public readonly record struct TerrainLiquidSprite(
    TextureFrameSequenceAsset Animation,
    SectorCoord SectorCoord,
    float IsoX,
    float IsoY,
    int Width,
    int Height,
    byte AlphaLeft,
    byte AlphaTop,
    byte AlphaRight,
    byte AlphaBottom,
    byte TextureVariant,
    float AnimationPeriodSeconds);

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
