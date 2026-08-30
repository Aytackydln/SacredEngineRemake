using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene.InGame;
using Sacred.Shaders;
using Sacred.World.Geometry;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Sprites;

/// <summary>Builds compact frame-local GPU streams for sprites and their atlas-backed shadows.</summary>
internal sealed class Dx12SpriteInstanceBuilder
{
    private const uint LiquidSpriteFlag = 0x01;
    private const uint TransposeTextureFlag = 0x02;
    private const uint MixedLightEmitterFlag = 0x20000000;
    private const uint ParticleSpriteFlag = 0x40000000;
    private const uint UnlitSpriteFlag = 0x80000000;
    private const uint DirectionalShadowFlag = 0x00000100;
    private const float PainterDepthScale = 1.0f / 4096.0f;
    private static readonly int SpriteInstanceStride = Marshal.SizeOf<StaticSpriteInstance>();
    private static readonly int ShadowInstanceStride = Marshal.SizeOf<StaticSpriteShadowInstance>();

    private readonly ID3D12Device _device;
    private readonly Dx12SpriteTextureCache _textureCache;
    private readonly Dx12SpriteFrameState[] _frameStates;
    private IReadOnlyList<LiquidSpriteDrawRange> _activeLiquidRanges = Array.Empty<LiquidSpriteDrawRange>();

    public Dx12SpriteInstanceBuilder(
        ID3D12Device device,
        Dx12SpriteTextureCache textureCache,
        int frameCount)
    {
        _device = device;
        _textureCache = textureCache;
        _frameStates = new Dx12SpriteFrameState[frameCount];
        for (var index = 0; index < frameCount; index++)
            _frameStates[index] = new Dx12SpriteFrameState();
    }

    public int CandidateLiquidSpriteCount { get; private set; }
    public int VisibleLiquidSpriteCount { get; private set; }
    public int CandidateStaticSpriteCount { get; private set; }
    public int VisibleStaticSpriteCount { get; private set; }
    public int VisibleStaticShadowCount { get; private set; }
    public int LegacyShadowDrawCallCount { get; private set; }

    public unsafe WorldSpriteBatch Prepare(
        SacredCamera camera,
        IReadOnlyList<TerrainLiquidSprite> liquidSprites,
        IReadOnlyList<TerrainStaticSprite> staticSprites,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight,
        ulong spriteRevision)
    {
        var state = _frameStates[frame.Index];
        CandidateLiquidSpriteCount = liquidSprites.Count;
        CandidateStaticSpriteCount = staticSprites.Count;
        if (state.Matches(
                spriteRevision,
                _textureCache.ResidencyRevision,
                camera.WorldCenter,
                camera.ViewportZoom,
                renderWidth,
                renderHeight))
        {
            _activeLiquidRanges = state.LiquidRanges;
            ApplyCounts(state.Batch, state.LiquidInstanceCount);
            return state.Batch;
        }

        state.LiquidRanges.Clear();
        _activeLiquidRanges = state.LiquidRanges;
        if (liquidSprites.Count == 0 && staticSprites.Count == 0)
        {
            state.Remember(
                spriteRevision,
                _textureCache.ResidencyRevision,
                camera.WorldCenter,
                camera.ViewportZoom,
                renderWidth,
                renderHeight,
                default,
                0);
            ApplyCounts(default, 0);
            return default;
        }

        var screenTransform = IsometricProjection.CreateScreenTransform(
            camera.WorldCenter,
            camera.ViewportZoom,
            renderWidth,
            renderHeight);
        frame.EnsureSpriteInstanceCapacity(
            _device,
            SpriteInstanceStride,
            liquidSprites.Count + staticSprites.Count);
        if (staticSprites.Count > 0)
        {
            frame.EnsureStaticShadowInstanceCapacity(
                _device,
                ShadowInstanceStride,
                staticSprites.Count);
        }

        var instances = (StaticSpriteInstance*)frame.SpriteInstanceBufferMapped;
        var shadowInstances = staticSprites.Count > 0
            ? (StaticSpriteShadowInstance*)frame.StaticShadowInstanceBufferMapped
            : null;
        var instanceCount = 0;
        var rangeStart = 0;
        SectorCoord? rangeSector = null;

        for (var index = 0; index < liquidSprites.Count; index++)
        {
            var sprite = liquidSprites[index];
            if (rangeSector != sprite.SectorCoord)
            {
                if (rangeSector is not null && instanceCount > rangeStart)
                {
                    state.LiquidRanges.Add(new LiquidSpriteDrawRange(
                        rangeSector.Value,
                        rangeStart,
                        instanceCount - rangeStart));
                }

                rangeSector = sprite.SectorCoord;
                rangeStart = instanceCount;
            }

            if (!_textureCache.TryGetLiquidSlot(sprite.Animation.Name, out var textureSlot))
                continue;

            var drawPosition = screenTransform.ToScreen(sprite.IsoX, sprite.IsoY);
            var drawWidth = screenTransform.Scale(sprite.Width);
            var drawHeight = screenTransform.Scale(sprite.Height);
            if (!IntersectsViewport(
                    drawPosition.X,
                    drawPosition.Y,
                    drawWidth,
                    drawHeight,
                    renderWidth,
                    renderHeight))
            {
                continue;
            }

            instances[instanceCount++] = new StaticSpriteInstance(
                drawPosition.X,
                drawPosition.Y,
                drawWidth,
                drawHeight,
                1.0f,
                textureSlot,
                (uint)sprite.Animation.FrameCount,
                LiquidSpriteFlag | ((uint)sprite.TextureVariant << 1),
                sprite.AnimationPeriodSeconds,
                sprite.AlphaLeft / 255.0f,
                sprite.AlphaTop / 255.0f,
                sprite.AlphaRight / 255.0f,
                sprite.AlphaBottom / 255.0f,
                (uint)sprite.Animation.AtlasColumns,
                (uint)sprite.Animation.AtlasRows);
        }

        if (rangeSector is not null && instanceCount > rangeStart)
        {
            state.LiquidRanges.Add(new LiquidSpriteDrawRange(
                rangeSector.Value,
                rangeStart,
                instanceCount - rangeStart));
        }

        var staticStartInstance = instanceCount;
        var shadowInstanceCount = 0;
        var legacyShadowDrawCallCount = 0;
        var previousLegacyInstanceCastsShadow = false;
        StaticSpriteAsset? shadowAtlas = null;
        var shadowTextureReady = false;
        var shadowTextureSlot = 0u;
        var shadowAtlasTexelSize = Vector2.Zero;

        for (var index = 0; index < staticSprites.Count; index++)
        {
            var sprite = staticSprites[index];
            if (!_textureCache.TryGetStaticSlot(sprite.Sprite, out var textureSlot))
                continue;

            var drawPosition = screenTransform.ToScreen(sprite.IsoX, sprite.IsoY);
            var drawWidth = screenTransform.Scale(sprite.RenderWidth);
            var drawHeight = screenTransform.Scale(sprite.RenderHeight);
            var spriteVisible = IntersectsViewport(
                drawPosition.X,
                drawPosition.Y,
                drawWidth,
                drawHeight,
                renderWidth,
                renderHeight);

            var hasShadow = false;
            var shadowVisible = false;
            var shadowRoot = Vector2.Zero;
            var shadowContactExtent = 0.0f;
            var shadowProjectionLength = 0.0f;
            TerrainStaticShadow shadow = default;
            if (sprite.Shadow is { } candidateShadow)
            {
                if (shadowAtlas is null)
                {
                    shadowAtlas = candidateShadow.Atlas;
                    shadowTextureReady = _textureCache.TryGetStaticSlot(
                        shadowAtlas,
                        out shadowTextureSlot);
                    if (shadowTextureReady)
                    {
                        shadowAtlasTexelSize = new Vector2(
                            1.0f / Math.Max(1, shadowAtlas.AtlasWidth),
                            1.0f / Math.Max(1, shadowAtlas.AtlasHeight));
                    }
                }

                if (shadowTextureReady)
                {
                    shadow = candidateShadow;
                    hasShadow = true;
                    shadowRoot = screenTransform.ToScreen(
                        sprite.DepthX + shadow.RootOffsetX,
                        sprite.DepthY + shadow.RootOffsetY);
                    shadowContactExtent = screenTransform.Scale(shadow.ContactExtent);
                    shadowProjectionLength = screenTransform.Scale(shadow.ProjectionLength);
                    var shadowCullRadius = MathF.Abs(shadowContactExtent) +
                                           MathF.Abs(shadowProjectionLength) * 1.75f;
                    shadowVisible = shadowCullRadius > 0.0f && IntersectsViewport(
                        shadowRoot.X - shadowCullRadius,
                        shadowRoot.Y - shadowCullRadius,
                        shadowCullRadius * 2.0f,
                        shadowCullRadius * 2.0f,
                        renderWidth,
                        renderHeight);
                }
            }

            if (!spriteVisible && !shadowVisible)
                continue;

            // The former shared sprite/shadow stream issued a new draw whenever a
            // non-caster interrupted the painter-ordered list. Retain that count as
            // live evidence for the batching improvement in the debug overlay.
            if (hasShadow)
            {
                if (!previousLegacyInstanceCastsShadow)
                    legacyShadowDrawCallCount++;
                previousLegacyInstanceCastsShadow = true;
            }
            else
            {
                previousLegacyInstanceCastsShadow = false;
            }

            if (spriteVisible)
            {
                instances[instanceCount++] = new StaticSpriteInstance(
                    drawPosition.X,
                    drawPosition.Y,
                    drawWidth,
                    drawHeight,
                    CalculateSceneDepth(camera, sprite),
                    textureSlot,
                    (uint)sprite.Sprite.FrameCount,
                    (sprite.IsUnlit ? UnlitSpriteFlag : 0) |
                    (sprite.IsParticleSprite ? ParticleSpriteFlag : 0) |
                    (sprite.IsMixedLightEmitter ? MixedLightEmitterFlag : 0) |
                    (sprite.TransposeTexture ? TransposeTextureFlag : 0),
                    sprite.Sprite.AnimationPeriodSeconds,
                    1.0f,
                    1.0f,
                    1.0f,
                    1.0f,
                    (uint)sprite.Sprite.AtlasColumns,
                    (uint)sprite.Sprite.AtlasRows);
            }

            if (shadowVisible)
            {
                var atlasCellAndProjection = (uint)shadow.AtlasCellIndex;
                if (shadow.Projection == SacredItemStaticShadowProjection.Directional)
                    atlasCellAndProjection |= DirectionalShadowFlag;
                shadowInstances![shadowInstanceCount++] = new StaticSpriteShadowInstance(
                    shadowRoot.X,
                    shadowRoot.Y,
                    shadowContactExtent,
                    shadowProjectionLength,
                    atlasCellAndProjection);
            }
        }

        var batch = new WorldSpriteBatch(
            staticStartInstance,
            instanceCount - staticStartInstance,
            shadowInstanceCount,
            shadowTextureSlot,
            shadowAtlasTexelSize,
            legacyShadowDrawCallCount);
        state.Remember(
            spriteRevision,
            _textureCache.ResidencyRevision,
            camera.WorldCenter,
            screenTransform.Zoom,
            renderWidth,
            renderHeight,
            batch,
            staticStartInstance);
        ApplyCounts(batch, staticStartInstance);
        return batch;
    }

    public bool TryGetLiquidRange(SectorCoord coord, out LiquidSpriteDrawRange range)
    {
        foreach (var candidate in _activeLiquidRanges)
        {
            if (candidate.Coord != coord)
                continue;
            range = candidate;
            return true;
        }

        range = default;
        return false;
    }

    private void ApplyCounts(WorldSpriteBatch batch, int liquidInstanceCount)
    {
        VisibleLiquidSpriteCount = liquidInstanceCount;
        VisibleStaticSpriteCount = batch.StaticInstanceCount;
        VisibleStaticShadowCount = batch.ShadowInstanceCount;
        LegacyShadowDrawCallCount = batch.LegacyShadowDrawCallCount;
    }

    private static float CalculateSceneDepth(SacredCamera camera, TerrainStaticSprite sprite)
    {
        var depthKey = sprite.TileDepth +
                       sprite.TileWorldY * 0.001f +
                       sprite.TileWorldX * 0.000001f +
                       sprite.ChainDepth * 0.0000001f;
        var centerDepthKey = camera.WorldCenter.X + camera.WorldCenter.Y + camera.WorldCenter.Y * 0.001f;
        return Math.Clamp(0.50f - (depthKey - centerDepthKey) * PainterDepthScale, 0.20f, 0.72f);
    }

    private static bool IntersectsViewport(
        float x,
        float y,
        float width,
        float height,
        int renderWidth,
        int renderHeight) =>
        x < renderWidth && y < renderHeight && x + width > 0.0f && y + height > 0.0f;
}
