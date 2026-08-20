using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Core.World.Sector;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene.InGame;
using Sacred.Shaders;
using Sacred.World.Geometry;
using Vortice.Direct3D;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Sprites;

/// <summary>Owns sprite texture residency, frame-local instances, and batched draw recording.</summary>
internal sealed class Dx12SpritePass : IDisposable
{
    public const int MaximumTextureCount = Dx12SpriteTextureCache.MaximumTextureCount;

    public int CandidateLiquidSpriteCount { get; private set; }
    public int VisibleLiquidSpriteCount { get; private set; }
    public int CandidateStaticSpriteCount { get; private set; }
    public int VisibleStaticSpriteCount { get; private set; }

    private const uint LiquidSpriteFlag = 0x01;
    private const uint TransposeTextureFlag = 0x02;
    private const uint MixedLightEmitterFlag = 0x20000000;
    private const uint ParticleSpriteFlag = 0x40000000;
    private const uint UnlitSpriteFlag = 0x80000000;
    private const float AlphaCutoff = 0.45f;
    private const float PainterDepthScale = 1.0f / 4096.0f;
    private static readonly int InstanceStride = Marshal.SizeOf<StaticSpriteInstance>();

    private readonly ID3D12Device _device;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly GpuDescriptorHandle _srvHeapGpuStart;
    private readonly int _descriptorSize;
    private readonly int _firstTextureSrvSlot;
    private readonly Dx12SpriteTextureCache _textureCache;
    private readonly SpriteFrameState[] _frameStates;
    private readonly StaticSpriteShaderConstantsUpdater _shaderConstants = new();
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private IReadOnlyList<LiquidSpriteDrawRange> _activeLiquidRanges = Array.Empty<LiquidSpriteDrawRange>();

    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _staticPipeline;
    private ID3D12PipelineState? _liquidPipeline;

    public Dx12SpritePass(
        ID3D12Device device,
        ID3D12GraphicsCommandList commandList,
        Dx12TextureUploader uploader,
        ID3D12DescriptorHeap srvHeap,
        int descriptorSize,
        int firstTextureSrvSlot,
        int frameCount)
    {
        _device = device;
        _commandList = commandList;
        _srvHeapGpuStart = srvHeap.GetGPUDescriptorHandleForHeapStart();
        _descriptorSize = descriptorSize;
        _firstTextureSrvSlot = firstTextureSrvSlot;
        _textureCache = new Dx12SpriteTextureCache(
            uploader,
            commandList,
            srvHeap,
            descriptorSize,
            firstTextureSrvSlot);
        _frameStates = new SpriteFrameState[frameCount];
        for (var index = 0; index < frameCount; index++)
            _frameStates[index] = new SpriteFrameState();
    }

    public void SetPipeline(Dx12CreatedPipelineGroup pipeline)
    {
        _rootSignature = pipeline.RootSignature;
        _staticPipeline = pipeline[Dx12PipelineKind.StaticSprite];
        _liquidPipeline = pipeline[Dx12PipelineKind.LiquidSprite];
    }

    public void DisposePipeline()
    {
        _staticPipeline?.Dispose();
        _staticPipeline = null;
        _liquidPipeline?.Dispose();
        _liquidPipeline = null;
        _rootSignature?.Dispose();
        _rootSignature = null;
    }

    public void PrepareTextures(
        IReadOnlyList<TerrainLiquidSprite> liquidSprites,
        IReadOnlyList<TerrainStaticSprite> staticSprites,
        Dx12FrameContext frame,
        ulong spriteRevision)
    {
        _textureCache.Prepare(liquidSprites, staticSprites, frame, spriteRevision);
    }

    public bool VisibleTexturesPrepared(ulong spriteRevision) =>
        _textureCache.IsPrepared(spriteRevision);

    public unsafe WorldSpriteBatch PrepareInstances(
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
            VisibleLiquidSpriteCount = state.LiquidInstanceCount;
            VisibleStaticSpriteCount = state.Batch.StaticInstanceCount;
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
            VisibleLiquidSpriteCount = 0;
            VisibleStaticSpriteCount = 0;
            return default;
        }

        // Match the terrain pass bit-for-bit at the float boundary. The transform preserves
        // its origin in double precision until a shader instance is written.
        var screenTransform = IsometricProjection.CreateScreenTransform(
            camera.WorldCenter,
            camera.ViewportZoom,
            renderWidth,
            renderHeight);
        frame.EnsureSpriteInstanceCapacity(
            _device,
            InstanceStride,
            liquidSprites.Count + staticSprites.Count);

        var instances = (StaticSpriteInstance*)frame.SpriteInstanceBufferMapped;
        var instanceCount = 0;
        var rangeStart = 0;
        SectorCoord? rangeSector = null;

        for (var index = 0; index < liquidSprites.Count; index++)
        {
            var sprite = liquidSprites[index];
            if (rangeSector != sprite.SectorCoord)
            {
                if (rangeSector is not null && instanceCount > rangeStart)
                    state.LiquidRanges.Add(new LiquidSpriteDrawRange(
                        rangeSector.Value,
                        rangeStart,
                        instanceCount - rangeStart));

                rangeSector = sprite.SectorCoord;
                rangeStart = instanceCount;
            }

            if (!_textureCache.TryGetLiquidSlot(sprite.Animation.Name, out var textureSlot))
                continue;

            var drawPosition = screenTransform.ToScreen(sprite.IsoX, sprite.IsoY);
            var drawX = drawPosition.X;
            var drawY = drawPosition.Y;
            var drawWidth = screenTransform.Scale(sprite.Width);
            var drawHeight = screenTransform.Scale(sprite.Height);
            if (!IntersectsViewport(drawX, drawY, drawWidth, drawHeight, renderWidth, renderHeight))
                continue;

            instances[instanceCount++] = new StaticSpriteInstance(
                drawX,
                drawY,
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
            state.LiquidRanges.Add(new LiquidSpriteDrawRange(rangeSector.Value, rangeStart, instanceCount - rangeStart));

        var staticStartInstance = instanceCount;
        for (var index = 0; index < staticSprites.Count; index++)
        {
            var sprite = staticSprites[index];
            if (!_textureCache.TryGetStaticSlot(sprite.Sprite, out var textureSlot))
                continue;

            var drawPosition = screenTransform.ToScreen(sprite.IsoX, sprite.IsoY);
            var drawX = drawPosition.X;
            var drawY = drawPosition.Y;
            var drawWidth = screenTransform.Scale(sprite.RenderWidth);
            var drawHeight = screenTransform.Scale(sprite.RenderHeight);
            if (!IntersectsViewport(drawX, drawY, drawWidth, drawHeight, renderWidth, renderHeight))
                continue;

            instances[instanceCount++] = new StaticSpriteInstance(
                drawX,
                drawY,
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

        var batch = new WorldSpriteBatch(staticStartInstance, instanceCount - staticStartInstance);
        VisibleLiquidSpriteCount = staticStartInstance;
        VisibleStaticSpriteCount = batch.StaticInstanceCount;
        state.Remember(
            spriteRevision,
            _textureCache.ResidencyRevision,
            camera.WorldCenter,
            screenTransform.Zoom,
            renderWidth,
            renderHeight,
            batch,
            staticStartInstance);
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

    public void RecordLiquid(
        LiquidSpriteDrawRange range,
        Vector3 ambientColour,
        float paperWhiteNits,
        int worldLightCount,
        float nightBlend,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight) =>
        RecordBatch(
            range.StartInstance,
            range.InstanceCount,
            _liquidPipeline,
            ambientColour,
            paperWhiteNits,
            paperWhiteNits,
            worldLightCount,
            nightBlend,
            frame,
            renderWidth,
            renderHeight);

    public void RecordStatic(
        WorldSpriteBatch batch,
        Vector3 ambientColour,
        float paperWhiteNits,
        float unlitWhiteNits,
        int worldLightCount,
        float nightBlend,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight) =>
        RecordBatch(
            batch.StaticStartInstance,
            batch.StaticInstanceCount,
            _staticPipeline,
            ambientColour,
            paperWhiteNits,
            unlitWhiteNits,
            worldLightCount,
            nightBlend,
            frame,
            renderWidth,
            renderHeight);

    public void Dispose()
    {
        _textureCache.Dispose();
    }

    private unsafe void RecordBatch(
        int startInstance,
        int instanceCount,
        ID3D12PipelineState? pipeline,
        Vector3 ambientColour,
        float paperWhiteNits,
        float unlitWhiteNits,
        int worldLightCount,
        float nightBlend,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight)
    {
        if (instanceCount == 0 || pipeline is null || _rootSignature is null)
            return;

        var sceneConstants = stackalloc float[StaticSpriteShaderLayout.SceneConstantsCount];
        _shaderConstants.Write(
            sceneConstants,
            new StaticSpriteSceneConstants(
                new Vector2(renderWidth, renderHeight),
                AlphaCutoff,
                ambientColour,
                paperWhiteNits,
                unlitWhiteNits,
                (float)Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds,
                worldLightCount,
                nightBlend));

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(pipeline);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commandList.SetGraphicsRoot32BitConstants(
            StaticSpriteShaderLayout.SceneConstantsRootParameter,
            StaticSpriteShaderLayout.SceneConstantsCount,
            sceneConstants,
            0);
        _commandList.SetGraphicsRootShaderResourceView(
            StaticSpriteShaderLayout.WorldLightBufferRootParameter,
            frame.LightHaloInstanceBuffer.GPUVirtualAddress);
        var instances = (StaticSpriteInstance*)frame.SpriteInstanceBufferMapped + startInstance;
        var firstInstance = 0;
        while (firstInstance < instanceCount)
        {
            var textureSlot = instances[firstInstance].TextureIndex;
            var runLength = 1;
            while (firstInstance + runLength < instanceCount &&
                   instances[firstInstance + runLength].TextureIndex == textureSlot)
                runLength++;

            _commandList.SetGraphicsRootDescriptorTable(
                StaticSpriteShaderLayout.TextureTableRootParameter,
                SrvGpuHandle(_firstTextureSrvSlot + (int)textureSlot));
            _commandList.SetGraphicsRootShaderResourceView(
                StaticSpriteShaderLayout.InstanceBufferRootParameter,
                frame.SpriteInstanceBuffer.GPUVirtualAddress +
                (ulong)((startInstance + firstInstance) * InstanceStride));
            _commandList.DrawInstanced(6, (uint)runLength, 0, 0);
            firstInstance += runLength;
        }
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

    private GpuDescriptorHandle SrvGpuHandle(int index) => _srvHeapGpuStart + index * _descriptorSize;

    private sealed class SpriteFrameState
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
}

internal readonly record struct WorldSpriteBatch(
    int StaticStartInstance,
    int StaticInstanceCount);
internal readonly record struct LiquidSpriteDrawRange(SectorCoord Coord, int StartInstance, int InstanceCount);
