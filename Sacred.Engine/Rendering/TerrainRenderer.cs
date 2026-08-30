using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;

namespace Sacred.Engine.Rendering;

public sealed class TerrainRenderer
{
    private const int MaxConcurrentSectorImageBuilds = 1;

    private readonly SectorCompositionBuilder _sectorCompositionBuilder;
    private readonly TerrainLiquidSpriteBuilder _liquidSpriteBuilder;
    private readonly TerrainStaticSpriteBuilder _staticSpriteBuilder;
    private readonly Dictionary<SectorCoord, TerrainSectorComposition> _sectorCache = new();
    private readonly Dictionary<SectorCoord, Task<TerrainSectorComposition>> _sectorBuildTasks = new();
    private readonly List<TerrainSectorComposition> _visibleSectorImages = new(9);
    private readonly List<Sector> _candidateSectors = new(9);
    private readonly HashSet<SectorCoord> _neededSectorCoords = new();
    private readonly List<SectorCoord> _sectorCoordsToRemove = new(9);
    private readonly List<SectorCoord> _completedSectorBuilds = new(9);
    private VisibleWorld? _preparedWorld;
    private IndoorTileGroup? _activeIndoorGroup;
    private bool _worldChangedThisFrame;
    private bool _indoorChangedThisFrame;

    public TerrainRenderStats LastStats { get; private set; }
    public ulong WorldSpriteRevision { get; private set; }
    public IReadOnlyList<TerrainWorldLight> VisibleWorldLights { get; private set; } = [];
    public bool HasPendingSpriteAssetRequests =>
        _staticSpriteBuilder.HasPendingAssetRequests || _liquidSpriteBuilder.HasPendingAssetRequests;

    public TerrainRenderer(AssetManager assets)
    {
        _sectorCompositionBuilder = new SectorCompositionBuilder(assets);
        _liquidSpriteBuilder = new TerrainLiquidSpriteBuilder(assets);
        _staticSpriteBuilder = new TerrainStaticSpriteBuilder(assets);
    }

    public IReadOnlyList<TerrainSectorComposition> PrepareVisibleWorld(
        VisibleWorld world,
        IndoorTileGroup? activeIndoorGroup = null)
    {
        var previousIndoorGroup = _activeIndoorGroup;
        var indoorChanged = previousIndoorGroup?.Id != activeIndoorGroup?.Id;
        var worldChanged = !ReferenceEquals(_preparedWorld, world);
        _activeIndoorGroup = activeIndoorGroup;
        _worldChangedThisFrame = worldChanged;
        _indoorChangedThisFrame = indoorChanged;
        if (worldChanged)
        {
            _preparedWorld = world;
            SelectCandidateSectors(world);
            PruneSectorCache();
        }
        var sectorBuildCompleted = PumpCompletedSectorBuilds();
        if (!worldChanged && !sectorBuildCompleted)
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
            _sectorCompositionBuilder.CachedTileCount,
            floorCandidateTiles,
            drawnFloorTiles,
            missingFloorTiles,
            _sectorCompositionBuilder.CachedFloorCount,
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
        var preparation = _staticSpriteBuilder.Prepare(
            _candidateSectors,
            _worldChangedThisFrame || _indoorChangedThisFrame,
            _activeIndoorGroup,
            true);
        if (!preparation.Changed)
            return preparation.Sprites;

        VisibleWorldLights = preparation.Lights;
        WorldSpriteRevision++;
        LastStats = LastStats with
        {
            StaticCandidateObjects = preparation.CandidateObjects,
            StaticDrawnObjects = preparation.Sprites.Count,
            StaticMissingObjects = preparation.MissingObjects
        };
        return preparation.Sprites;
    }

    public IReadOnlyList<TerrainLiquidSprite> PrepareVisibleLiquidSprites()
    {
        var preparation = _liquidSpriteBuilder.Prepare(_candidateSectors, _worldChangedThisFrame);
        if (!preparation.Changed)
            return preparation.Sprites;

        WorldSpriteRevision++;
        LastStats = LastStats with
        {
            LiquidCandidateTiles = preparation.CandidateTiles,
            LiquidDrawnTiles = preparation.Sprites.Count,
            LiquidMissingTiles = preparation.MissingTiles
        };
        return preparation.Sprites;
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
            if (task.Status == TaskStatus.RanToCompletion &&
                _neededSectorCoords.Contains(coord))
            {
                _sectorCache[coord] = task.Result;
            }
        }

        return true;
    }

    private TerrainSectorComposition? GetSectorImageOrQueueBuild(Sector sector)
    {
        if (_sectorCache.TryGetValue(sector.Coord, out var cached))
            return cached;

        if (!_sectorBuildTasks.ContainsKey(sector.Coord) && CountPendingSectorBuilds() < MaxConcurrentSectorImageBuilds)
        {
            _sectorBuildTasks[sector.Coord] = Task.Run(
                () => _sectorCompositionBuilder.BuildAsync(sector));
        }

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

}

public readonly record struct TerrainStaticSprite(
    StaticSpriteAsset Sprite,
    bool IsUnlit,
    bool IsParticleSprite,
    bool IsMixedLightEmitter,
    bool TransposeTexture,
    TerrainStaticShadow? Shadow,
    float RenderWidth,
    float RenderHeight,
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

public readonly record struct TerrainStaticShadow(
    StaticSpriteAsset Atlas,
    float RootOffsetX,
    float RootOffsetY,
    float ContactExtent,
    float ProjectionLength,
    ushort AtlasCellIndex,
    SacredItemStaticShadowProjection Projection);

public readonly record struct TerrainWorldLight(
    float IsoX,
    float IsoY,
    float Diameter,
    Vector3 Colour,
    float Opacity,
    WorldLightShape Shape,
    StaticSpriteAsset? Mask = null);

public enum WorldLightShape : uint
{
    RadialHalo,
    SparkleCluster,
    /// <summary>
    /// Invisible Items.pak light volume. It contributes to surface lighting but
    /// never draws a visible particle or halo of its own.
    /// </summary>
    SurfaceIllumination
}

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
