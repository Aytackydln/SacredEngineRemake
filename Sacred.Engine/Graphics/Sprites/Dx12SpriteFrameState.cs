using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.World.Sector;

namespace Sacred.Engine.Graphics.Sprites;

/// <summary>Caches frame-local sprite ranges while the world view is unchanged.</summary>
internal sealed class Dx12SpriteFrameState
{
    private ulong _spriteRevision;
    private ulong _residencyRevision;
    private Vector2 _worldCenter;
    private float _viewportZoom;
    private int _renderWidth;
    private int _renderHeight;
    private bool _valid;

    public List<LiquidSpriteDrawRange> LiquidRanges { get; } = new(9);
    public WorldSpriteBatch Batch { get; private set; }
    public int LiquidInstanceCount { get; private set; }

    public bool Matches(
        ulong spriteRevision,
        ulong residencyRevision,
        Vector2 worldCenter,
        float viewportZoom,
        int renderWidth,
        int renderHeight) =>
        _valid &&
        _spriteRevision == spriteRevision &&
        _residencyRevision == residencyRevision &&
        _worldCenter == worldCenter &&
        _viewportZoom == viewportZoom &&
        _renderWidth == renderWidth &&
        _renderHeight == renderHeight;

    public void Remember(
        ulong spriteRevision,
        ulong residencyRevision,
        Vector2 worldCenter,
        float viewportZoom,
        int renderWidth,
        int renderHeight,
        WorldSpriteBatch batch,
        int liquidInstanceCount)
    {
        _spriteRevision = spriteRevision;
        _residencyRevision = residencyRevision;
        _worldCenter = worldCenter;
        _viewportZoom = viewportZoom;
        _renderWidth = renderWidth;
        _renderHeight = renderHeight;
        Batch = batch;
        LiquidInstanceCount = liquidInstanceCount;
        _valid = true;
    }
}

internal readonly record struct WorldSpriteBatch(
    int StaticStartInstance,
    int StaticInstanceCount,
    int ShadowInstanceCount,
    uint ShadowTextureSlot,
    Vector2 ShadowAtlasTexelSize,
    int LegacyShadowDrawCallCount);

internal readonly record struct LiquidSpriteDrawRange(
    SectorCoord Coord,
    int StartInstance,
    int InstanceCount);
