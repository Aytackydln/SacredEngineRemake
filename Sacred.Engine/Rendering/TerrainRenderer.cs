using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.World.Particles;

namespace Sacred.Engine.Rendering;

public sealed class TerrainRenderer : IDisposable
{
    private const int MaxConcurrentSectorImageBuilds = 1;

    private readonly SectorCompositionBuilder _sectorCompositionBuilder;
    private readonly TerrainPrefetchPreparation _prefetchPreparation;
    private readonly List<TerrainSectorComposition> _preloadedSectorImages = new(3);
    private bool _cacheInvalidated;
    private readonly TerrainLiquidSpriteBuilder _liquidSpriteBuilder;
    private readonly TerrainStaticSpriteBuilder _staticSpriteBuilder;
    private readonly TerrainParticleSpriteBuilder _particleSpriteBuilder;
    private readonly PrioritizedAssetLoadScheduler _sectorBuildScheduler =
        new("Sacred sector builder");
    private readonly Dictionary<SectorCoord, TerrainSectorComposition> _sectorCache = new();
    private readonly Dictionary<SectorCoord, Task<TerrainSectorComposition>> _sectorBuildTasks = new();
    private readonly List<TerrainSectorComposition> _visibleSectorImages = new(9);
    private readonly List<Sector> _candidateSectors = new(9);
    private readonly List<TerrainStaticSprite> _visibleWorldSprites = new(1536);
    private readonly HashSet<SectorCoord> _neededSectorCoords = new();
    private readonly List<SectorCoord> _sectorCoordsToRemove = new(9);
    private readonly List<SectorCoord> _completedSectorBuilds = new(9);
    private VisibleWorld? _preparedWorld;
    private IndoorTileGroup? _activeIndoorGroup;
    private bool _worldChangedThisFrame;
    private bool _indoorChangedThisFrame;
    private bool _sectorCompositionsChangedThisFrame;
    private ulong _preparedParticleRevision = ulong.MaxValue;

    public IReadOnlyList<TerrainSectorComposition> PreloadedSectorImages => _preloadedSectorImages;

    public TerrainRenderStats LastStats { get; private set; }
    public ulong WorldSpriteRevision { get; private set; }
    public IReadOnlyList<TerrainWorldLight> VisibleWorldLights { get; private set; } = [];
    public bool HasPendingSpriteAssetRequests =>
        _staticSpriteBuilder.HasPendingAssetRequests ||
        _liquidSpriteBuilder.HasPendingAssetRequests ||
        _particleSpriteBuilder.HasPendingAssetRequests;

    public TerrainRenderer(AssetManager assets)
    {
        _sectorCompositionBuilder = new SectorCompositionBuilder(assets);
        _prefetchPreparation = new TerrainPrefetchPreparation(assets);
        _liquidSpriteBuilder = new TerrainLiquidSpriteBuilder(assets);
        _staticSpriteBuilder = new TerrainStaticSpriteBuilder(assets);
        _particleSpriteBuilder = new TerrainParticleSpriteBuilder(assets);
    }

    public IReadOnlyList<TerrainSectorComposition> PrepareVisibleWorld(
        VisibleWorld world,
        IndoorTileGroup? activeIndoorGroup = null)
    {
        var previousIndoorGroup = _activeIndoorGroup;
        var indoorChanged = previousIndoorGroup?.Id != activeIndoorGroup?.Id;
        var worldChanged = _cacheInvalidated || !SameVisibleSectors(_preparedWorld, world);
        _cacheInvalidated = false;
        _preparedWorld = world;
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
        _sectorCompositionsChangedThisFrame = worldChanged || sectorBuildCompleted;
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

    public IReadOnlyList<TerrainStaticSprite> PrepareVisibleStaticSprites(
        IReadOnlyList<WorldParticle>? particles = null,
        ulong particleRevision = 0)
    {
        var preparation = _staticSpriteBuilder.Prepare(
            _candidateSectors,
            _worldChangedThisFrame,
            _indoorChangedThisFrame,
            _activeIndoorGroup,
            true);
        if (preparation.PromotionsChanged || _sectorCompositionsChangedThisFrame)
        {
            for (var index = _visibleSectorImages.Count - 1; index >= 0; index--)
            {
                var composition = _visibleSectorImages[index];
                if (composition.NeedsRebuildForPromotions(preparation.PromotedEmbeddedObjectIds))
                {
                    InvalidateComposition(composition);
                    _visibleSectorImages.RemoveAt(index);
                }
                else composition.RemoveEmbeddedSprites(preparation.PromotedEmbeddedObjectIds);
            }
        }
        particles ??= Array.Empty<WorldParticle>();
        var particlesChanged = _preparedParticleRevision != particleRevision ||
                               _particleSpriteBuilder.HasPendingAssetRequests;
        if (!preparation.Changed && !particlesChanged)
            return _visibleWorldSprites;

        if (particlesChanged)
        {
            _particleSpriteBuilder.Prepare(particles);
            _preparedParticleRevision = particleRevision;
        }

        _visibleWorldSprites.Clear();
        _visibleWorldSprites.AddRange(preparation.Sprites);
        // Prefetched/retained plans remember which authored sprites were excluded from baking.
        for (var index = 0; index < _visibleWorldSprites.Count; index++)
        {
            var sprite = _visibleWorldSprites[index];
            if (!sprite.IsEmbeddedInTerrain) continue;
            var coord = new SectorCoord((int)MathF.Floor(sprite.TileWorldX / (float)Sector.TileCount),
                (int)MathF.Floor(sprite.TileWorldY / (float)Sector.TileCount));
            if (_sectorCache.TryGetValue(coord, out var composition) && composition.PromotedEmbeddedObjectIds.Contains(sprite.StaticObjectId))
                _visibleWorldSprites[index] = sprite with { IsEmbeddedInTerrain = false };
        }
        _visibleWorldSprites.AddRange(_particleSpriteBuilder.Sprites);

        if (preparation.Changed)
        {
            VisibleWorldLights = preparation.Lights;
            LastStats = LastStats with
            {
                StaticCandidateObjects = preparation.CandidateObjects,
                StaticMissingObjects = preparation.MissingObjects
            };
        }

        WorldSpriteRevision++;
        LastStats = LastStats with
        {
            StaticDrawnObjects = _visibleWorldSprites.Count
        };
        return _visibleWorldSprites;
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
            if (_preparedWorld is { } world && (Math.Abs(coord.X - world.CenterSector.X) > 2 || Math.Abs(coord.Y - world.CenterSector.Y) > 2))
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
                _preparedWorld is { } world && Math.Abs(coord.X - world.CenterSector.X) <= 2 && Math.Abs(coord.Y - world.CenterSector.Y) <= 2)
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
            _sectorBuildTasks[sector.Coord] = _sectorBuildScheduler.Schedule(
                AssetLoadPriority.Background,
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

    public void PreparePreloadedWorld(VisibleWorld world)
    {
        _preloadedSectorImages.Clear();
        if (_activeIndoorGroup is not null || _visibleSectorImages.Count != _candidateSectors.Count) return;
        var promotions = _prefetchPreparation.Prepare(world);
        foreach (var sector in world.PreloadedSectors)
        {
            var composition = GetSectorImageOrQueueBuild(sector);
            if (composition is null || promotions is null) continue;
            if (composition.NeedsRebuildForPromotions(promotions))
            {
                InvalidateComposition(composition);
                continue;
            }
            composition.RemoveEmbeddedSprites(promotions);
            _preloadedSectorImages.Add(composition);
        }
    }

    internal void InvalidateComposition(TerrainSectorComposition composition)
    {
        if (_sectorCache.TryGetValue(composition.Coord, out var cached) && ReferenceEquals(cached, composition))
        {
            _sectorCache.Remove(composition.Coord);
            _cacheInvalidated = true;
        }
    }

    private static bool SameVisibleSectors(VisibleWorld? previous, VisibleWorld current)
    {
        if (previous is null || previous.CenterSector != current.CenterSector || previous.Sectors.Count != current.Sectors.Count) return false;
        for (var index = 0; index < current.Sectors.Count; index++)
            if (!ReferenceEquals(previous.Sectors[index], current.Sectors[index])) return false;
        return true;
    }

    public void StopBackgroundWork()
    {
        _prefetchPreparation.Dispose();
        _sectorBuildScheduler.Dispose();
    }

    public void Dispose() => StopBackgroundWork();

}

public readonly record struct TerrainStaticSprite(
    StaticSpriteAsset Sprite,
    uint StaticObjectId,
    bool IsUnlit,
    bool IsParticleSprite,
    bool IsMixedLightEmitter,
    bool TransposeTexture,
    bool AllowsPlayerOcclusionFade,
    TerrainStaticShadow? Shadow,
    bool IsEmbeddedInTerrain,
    float RenderWidth,
    float RenderHeight,
    float IsoX,
    float IsoY,
    float DepthX,
    float DepthY,
    short SurfaceRenderLayer,
    bool IsIndoorSurface,
    int QueueIndex,
    int TileDepth,
    int TileWorldY,
    int TileWorldX,
    int ChainDepth,
    int InsertionOrder,
    float Opacity)
{
    /// <summary>Identifies an atlas-backed mini object from the Items.pak graphic type.</summary>
    public bool IsMiniObject { get; init; }
    public bool UsesSpriteDepth { get; init; }
    public byte HeightLevel { get; init; }
    /// <summary>Identifies scenery authored to render after the water pass.</summary>
    public bool RendersOverWater { get; init; }
    public uint ParticleColor { get; init; } = uint.MaxValue;
    public int ParticleAtlasCell { get; init; }
    public float ParticleRotation { get; init; }
    public uint ParticleBlendFlags { get; init; }
    /// <summary>Continuous camera depth for a moving particle, before normalization.</summary>
    public float? ParticleDepthKey { get; init; }
    /// <summary>Marks a file-authored mini-object animation such as a fixture flame.</summary>
    public bool IsAnimatedMiniObject { get; init; }
    /// <summary>Draws after world models and ignores their depth, as authored by Items.pak.</summary>
    public bool IsFrontLayer { get; init; }
    /// <summary>Sprites with fractional alpha must use alpha composition.</summary>
    public bool RequiresAlphaBlend =>
        IsParticleSprite || AllowsPlayerOcclusionFade || Sprite.HasTranslucentPixels;

    /// <summary>
    /// Ceiling art is deferred. Player-aware scenery retains its authored queue;
    /// its shader can reveal the player using the prepared occlusion map.
    /// </summary>
    public bool RequiresPostModelPass =>
        IsFrontLayer;
}

public readonly record struct TerrainStaticShadow(
    StaticSpriteAsset Atlas,
    float RootOffsetX,
    float RootOffsetY,
    float ContactExtent,
    float ProjectionExtent,
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
