using System.Collections.Generic;
using System.Threading;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Elevation;
using Sacred.Core.World.Lighting;
using Sacred.Core.World.Sector;

namespace Sacred.Engine.Rendering;

public sealed record TerrainTileSource(TextureAsset Texture, int SourceX, int SourceY);

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

public sealed class TerrainSectorComposition
{
    private TerrainCompositionTile[] _baseTiles;
    private TerrainCompositionTile[] _coverTiles;
    private TerrainCompositionTile[] _stairsDebugTiles;
    private TerrainCompositionTile[] _blockedAreaDebugTiles;
    private TerrainCompositionTile[] _terrainTopologyDebugTiles;

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
        _terrainTopologyDebugTiles = terrainTopologyDebugTiles;
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

    internal void ReleaseSourceTiles()
    {
        Interlocked.Exchange(ref _baseTiles, []);
        Interlocked.Exchange(ref _coverTiles, []);
        Interlocked.Exchange(ref _stairsDebugTiles, []);
        Interlocked.Exchange(ref _blockedAreaDebugTiles, []);
        Interlocked.Exchange(ref _terrainTopologyDebugTiles, []);
    }
}
