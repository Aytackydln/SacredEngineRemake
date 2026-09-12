using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Sector;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Shaders;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Sprites;

/// <summary>Owns sprite texture residency, frame-local instances, and batched draw recording.</summary>
internal sealed class Dx12SpritePass : IDisposable
{
    public const int MaximumTextureCount = Dx12SpriteTextureCache.MaximumTextureCount;

    private readonly Dx12SpriteTextureCache _textureCache;
    private readonly Dx12SpriteInstanceBuilder _instances;
    private readonly Dx12SpriteBatchRecorder _batchRecorder;
    private readonly Dx12StaticSpriteShadowPass _shadowPass;

    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _staticShadowPipeline;
    private ID3D12PipelineState? _staticPipeline;
    private ID3D12PipelineState? _transparentStaticPipeline;
    private ID3D12PipelineState? _unlitStaticPipeline;
    private ID3D12PipelineState? _transparentUnlitStaticPipeline;
    private ID3D12PipelineState? _liquidPipeline;
    private ID3D12PipelineState? _transparentParticleRgbPipeline;
    private ID3D12PipelineState? _transparentParticleArgbPipeline;
    private ID3D12PipelineState? _transparentParticleAlphaMaskPipeline;
    private bool _hdrOutput;

    public Dx12SpritePass(
        ID3D12Device device,
        ID3D12GraphicsCommandList commandList,
        Dx12TextureUploader uploader,
        ID3D12DescriptorHeap srvHeap,
        int descriptorSize,
        int firstTextureSrvSlot,
        GpuDescriptorHandle surfaceLightMap,
        GpuDescriptorHandle playerOcclusionMap,
        int frameCount)
    {
        var srvHeapGpuStart = srvHeap.GetGPUDescriptorHandleForHeapStart();
        _batchRecorder = new Dx12SpriteBatchRecorder(
            commandList,
            srvHeapGpuStart,
            descriptorSize,
            firstTextureSrvSlot,
            surfaceLightMap,
            playerOcclusionMap);
        _shadowPass = new Dx12StaticSpriteShadowPass(
            commandList,
            srvHeapGpuStart,
            descriptorSize,
            firstTextureSrvSlot);
        _textureCache = new Dx12SpriteTextureCache(
            uploader,
            commandList,
            srvHeap,
            descriptorSize,
            firstTextureSrvSlot);
        _instances = new Dx12SpriteInstanceBuilder(device, _textureCache, frameCount);
    }

    public int CandidateLiquidSpriteCount => _instances.CandidateLiquidSpriteCount;
    public int VisibleLiquidSpriteCount => _instances.VisibleLiquidSpriteCount;
    public int CandidateStaticSpriteCount => _instances.CandidateStaticSpriteCount;
    public int VisibleStaticSpriteCount => _instances.VisibleStaticSpriteCount;
    public int VisibleStaticShadowCount => _instances.VisibleStaticShadowCount;
    public int StaticShadowDrawCallCount => _shadowPass.DrawCallCount;
    public int LegacyShadowDrawCallCount => _instances.LegacyShadowDrawCallCount;

    public void SetPipeline(Dx12CreatedPipelineGroup pipeline, bool hdrOutput)
    {
        _hdrOutput = hdrOutput;
        _rootSignature = pipeline.RootSignature;
        _staticShadowPipeline = pipeline[Dx12PipelineKind.StaticSpriteShadow];
        _staticPipeline = pipeline[Dx12PipelineKind.StaticSprite];
        _transparentStaticPipeline = pipeline[Dx12PipelineKind.TransparentStaticSprite];
        _unlitStaticPipeline = pipeline[Dx12PipelineKind.UnlitStaticSprite];
        _transparentUnlitStaticPipeline = pipeline[Dx12PipelineKind.TransparentUnlitStaticSprite];
        _liquidPipeline = pipeline[Dx12PipelineKind.LiquidSprite];
        if (hdrOutput)
        {
            _transparentParticleRgbPipeline = pipeline[Dx12PipelineKind.TransparentUnlitParticleRgb];
            _transparentParticleArgbPipeline = pipeline[Dx12PipelineKind.TransparentUnlitParticleArgb];
            _transparentParticleAlphaMaskPipeline = pipeline[Dx12PipelineKind.TransparentUnlitParticleAlphaMask];
        }
        _batchRecorder.SetRootSignature(_rootSignature);
        _shadowPass.SetPipeline(_rootSignature, _staticShadowPipeline);
    }

    public void DisposePipeline()
    {
        _batchRecorder.ClearRootSignature();
        _shadowPass.ClearPipeline();
        _staticShadowPipeline?.Dispose();
        _staticShadowPipeline = null;
        _staticPipeline?.Dispose();
        _staticPipeline = null;
        _transparentStaticPipeline?.Dispose();
        _transparentStaticPipeline = null;
        _unlitStaticPipeline?.Dispose();
        _unlitStaticPipeline = null;
        _transparentUnlitStaticPipeline?.Dispose();
        _transparentUnlitStaticPipeline = null;
        _liquidPipeline?.Dispose();
        _liquidPipeline = null;
        _transparentParticleRgbPipeline?.Dispose();
        _transparentParticleRgbPipeline = null;
        _transparentParticleArgbPipeline?.Dispose();
        _transparentParticleArgbPipeline = null;
        _transparentParticleAlphaMaskPipeline?.Dispose();
        _transparentParticleAlphaMaskPipeline = null;
        _rootSignature?.Dispose();
        _rootSignature = null;
    }

    public void PrepareTextures(
        IReadOnlyList<TerrainLiquidSprite> liquidSprites,
        IReadOnlyList<TerrainStaticSprite> staticSprites,
        Dx12FrameContext frame,
        ulong spriteRevision) =>
        _textureCache.Prepare(liquidSprites, staticSprites, frame, spriteRevision);

    public bool VisibleTexturesPrepared(ulong spriteRevision) =>
        _textureCache.IsPrepared(spriteRevision);

    public WorldSpriteBatch PrepareInstances(
        SacredCamera camera,
        SceneModel? playerModel,
        IReadOnlyList<TerrainLiquidSprite> liquidSprites,
        IReadOnlyList<TerrainStaticSprite> staticSprites,
        uint? highlightedStaticObjectId,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight,
        ulong spriteRevision) =>
        _instances.Prepare(
            camera,
            playerModel,
            liquidSprites,
            staticSprites,
            highlightedStaticObjectId,
            frame,
            renderWidth,
            renderHeight,
            spriteRevision);

    public void RecordHighlightedStatic(
        WorldSpriteBatch batch,
        float highlightNits,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight) =>
        _batchRecorder.Record(
            batch.HighlightedStaticInstance,
            batch.HighlightedStaticInstance >= 0 ? 1 : 0,
            batch.HighlightedStaticIsUnlit ? _unlitStaticPipeline : _staticPipeline,
            Vector3.One,
            highlightNits,
            highlightNits,
            default,
            frame,
            renderWidth,
            renderHeight);

    public bool TryGetLiquidRange(SectorCoord coord, out LiquidSpriteDrawRange range) =>
        _instances.TryGetLiquidRange(coord, out range);

    public void RecordLiquid(
        LiquidSpriteDrawRange range,
        Vector3 ambientColour,
        float paperWhiteNits,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight) =>
        _batchRecorder.Record(
            range.StartInstance,
            range.InstanceCount,
            _liquidPipeline,
            ambientColour,
            paperWhiteNits,
            paperWhiteNits,
            default,
            frame,
            renderWidth,
            renderHeight);

    public void RecordOpaqueStatic(
        WorldSpriteBatch batch,
        Vector3 ambientColour,
        float paperWhiteNits,
        float unlitWhiteNits,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight) =>
        RecordStaticRanges(
            batch,
            false,
            ambientColour,
            paperWhiteNits,
            unlitWhiteNits,
            frame,
            renderWidth,
            renderHeight);

    public void RecordTransparentStatic(
        WorldSpriteBatch batch,
        Vector3 ambientColour,
        float paperWhiteNits,
        float unlitWhiteNits,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight) =>
        RecordStaticRanges(
            batch,
            true,
            ambientColour,
            paperWhiteNits,
            unlitWhiteNits,
            frame,
            renderWidth,
            renderHeight);

    private void RecordStaticRanges(
        WorldSpriteBatch batch,
        bool postModel,
        Vector3 ambientColour,
        float paperWhiteNits,
        float unlitWhiteNits,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight)
    {
        if (batch.StaticRanges is null)
            return;

        foreach (var range in batch.StaticRanges)
        {
            if (range.IsPostModel != postModel)
                continue;
            var pipeline = _hdrOutput && range.ParticleEncoding is { } encoding
                ? encoding switch
                {
                    SacredTextureChannelEncoding.AlphaMask => _transparentParticleAlphaMaskPipeline,
                    SacredTextureChannelEncoding.Argb => _transparentParticleArgbPipeline,
                    _ => _transparentParticleRgbPipeline
                }
                : (range.IsUnlit, range.RequiresAlphaBlend) switch
                {
                    (true, true) => _transparentUnlitStaticPipeline,
                    (true, false) => _unlitStaticPipeline,
                    (false, true) => _transparentStaticPipeline,
                    _ => _staticPipeline
                };
            _batchRecorder.Record(
                range.StartInstance,
                range.InstanceCount,
                pipeline,
                ambientColour,
                paperWhiteNits,
                unlitWhiteNits,
                batch.PlayerOcclusion,
                frame,
                renderWidth,
                renderHeight);
        }
    }

    public void RecordStaticShadows(
        WorldSpriteBatch batch,
        SacredCamera camera,
        SceneLighting lighting,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight) =>
        _shadowPass.Record(batch, camera, lighting, frame, renderWidth, renderHeight);

    public void Dispose() => _textureCache.Dispose();
}
