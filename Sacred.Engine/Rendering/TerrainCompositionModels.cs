using System.Collections.Generic;
using System.Threading;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Elevation;
using Sacred.Core.World.Lighting;
using Sacred.Core.World.Sector;

namespace Sacred.Engine.Rendering;

public sealed record TerrainTileSource(TextureAsset Texture, int SourceX, int SourceY)
{
    // Binary coverage uses an opaque cutout pass; resolve once before GPU submission.
    public bool IsOpaque { get; } = TerrainTileOpacity.IsOpaque(Texture, SourceX, SourceY);
}

public readonly record struct TerrainTileSurface(
    TerrainVisualElevationTile VisualElevation,
    TerrainBakedLightTile BakedLight)
{
    public static TerrainTileSurface FlatFullyLit => new(default, TerrainBakedLightTile.FullyLit);
}

public readonly record struct TerrainCompositionTile(
    int ScreenX,
    int ScreenY,
    TerrainTileSource Primary,
    TerrainTileSource? Secondary,
    TerrainTileSurface Surface);

/// <summary>A static shadow receiver rasterized once into its sector's terrain quad.</summary>
public readonly record struct TerrainEmbeddedSprite(
    StaticSpriteAsset Sprite,
    uint StaticObjectId,
    float ScreenX,
    float ScreenY);

public sealed class TerrainSectorComposition
{
    private TerrainCompositionTile[] _baseTiles;
    private TerrainCompositionTile[] _coverTiles;
    private TerrainCompositionTile[] _stairsDebugTiles;
    private TerrainCompositionTile[] _blockedAreaDebugTiles;
    private TerrainCompositionTile[] _terrainTopologyDebugTiles;
    private TerrainEmbeddedSprite[] _embeddedSprites;
    private int _embeddedSpriteCount;
    private readonly HashSet<uint> _embeddedObjectIds = [];
    private readonly HashSet<uint> _promotedEmbeddedObjectIds = [];
    private volatile bool _sourceTilesReleased;

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
        TerrainCompositionTile[] terrainTopologyDebugTiles,
        int terrainTopologyDebugOffsetX,
        int terrainTopologyDebugOffsetY,
        int terrainTopologyDebugWidth,
        int terrainTopologyDebugHeight,
        int groundCandidateTiles,
        int groundDrawnTiles,
        int groundMissingTiles,
        int floorCandidateTiles,
        int floorDrawnTiles,
        int floorMissingTiles,
        TerrainEmbeddedSprite[] embeddedSprites)
    {
        Coord = coord;
        IsoX = isoX;
        IsoY = isoY;
        Width = width;
        Height = height;
        Depth = depth;
        _baseTiles = baseTiles;
        _coverTiles = coverTiles;
        HasCoverTiles = coverTiles.Length != 0;
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
        _terrainTopologyDebugTiles = terrainTopologyDebugTiles;
        _embeddedSprites = embeddedSprites;
        _embeddedSpriteCount = embeddedSprites.Length;
        foreach (var sprite in embeddedSprites) _embeddedObjectIds.Add(sprite.StaticObjectId);
        TerrainTopologyDebugOffsetX = terrainTopologyDebugOffsetX;
        TerrainTopologyDebugOffsetY = terrainTopologyDebugOffsetY;
        TerrainTopologyDebugWidth = terrainTopologyDebugWidth;
        TerrainTopologyDebugHeight = terrainTopologyDebugHeight;
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
    // Retain this after source arrays are released by the GPU compositor.
    public bool HasCoverTiles { get; }
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
    public IReadOnlyList<TerrainCompositionTile> TerrainTopologyDebugTiles => _terrainTopologyDebugTiles;
    public int TerrainTopologyDebugOffsetX { get; }
    public int TerrainTopologyDebugOffsetY { get; }
    public int TerrainTopologyDebugWidth { get; }
    public int TerrainTopologyDebugHeight { get; }
    public int GroundCandidateTiles { get; }
    public int GroundDrawnTiles { get; }
    public int GroundMissingTiles { get; }
    public int FloorCandidateTiles { get; }
    public int FloorDrawnTiles { get; }
    public int FloorMissingTiles { get; }
    public IReadOnlyList<TerrainEmbeddedSprite> EmbeddedSprites => _embeddedSprites;
    public int EmbeddedSpriteCount => _embeddedSpriteCount;
    public IReadOnlySet<uint> PromotedEmbeddedObjectIds => _promotedEmbeddedObjectIds;

    internal bool NeedsRebuildForPromotions(IReadOnlySet<uint> objectIds)
    {
        if (!_sourceTilesReleased) return false;
        foreach (var objectId in objectIds)
            if (_embeddedObjectIds.Contains(objectId) && !_promotedEmbeddedObjectIds.Contains(objectId)) return true;
        return false;
    }

    internal void RemoveEmbeddedSprites(IReadOnlySet<uint> staticObjectIds)
    {
        if (staticObjectIds.Count == 0 || _embeddedSprites.Length == 0)
            return;

        var retainedCount = 0;
        foreach (var sprite in _embeddedSprites)
            if (!staticObjectIds.Contains(sprite.StaticObjectId))
                retainedCount++;
            else
                _promotedEmbeddedObjectIds.Add(sprite.StaticObjectId);

        if (retainedCount == _embeddedSprites.Length)
            return;

        var retained = new TerrainEmbeddedSprite[retainedCount];
        var destination = 0;
        foreach (var sprite in _embeddedSprites)
            if (!staticObjectIds.Contains(sprite.StaticObjectId))
                retained[destination++] = sprite;

        _embeddedSprites = retained;
        _embeddedSpriteCount = retainedCount;
    }

    // Debug plans remain available for on-demand drawing; only baked source data is released.
    internal void ReleaseSourceTiles()
    {
        _sourceTilesReleased = true;
        Interlocked.Exchange(ref _baseTiles, []);
        Interlocked.Exchange(ref _coverTiles, []);
        Interlocked.Exchange(ref _embeddedSprites, []);
    }
}
