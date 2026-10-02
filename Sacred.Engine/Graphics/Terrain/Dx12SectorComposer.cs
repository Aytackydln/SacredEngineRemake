using System;
using System.Collections.Generic;
using Sacred.Engine.Extern;
using Sacred.Engine.Graphics.Uploads;
using Sacred.Engine.Rendering;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Terrain;

/// <summary>
/// Rasterizes compact tile plans into persistent sector textures on a dedicated Direct3D queue.
/// A small context pool keeps resource reuse fence-safe while allowing two submissions in flight.
/// </summary>
internal sealed class Dx12SectorComposer : IDisposable
{
    public const int MaximumInFlightCompositions = 2;

    private const uint HasSecondaryMaskFlag = 0x01;
    private const uint PremultipliedOutputFlag = 0x02;
    private const Format OutputFormat = Format.R8G8B8A8_UNorm;

    private readonly ID3D12Device _device;
    private readonly Dx12SectorTargetPool _targets;
    private readonly ID3D12CommandQueue _commandQueue;
    private readonly ID3D12Fence _fence;
    private readonly Dx12SectorSourceTextureCache _sources;
    private readonly ID3D12RootSignature _rootSignature;
    private readonly ID3D12PipelineState _basePipeline;
    private readonly ID3D12PipelineState _coverPipeline;
    private readonly ID3D12PipelineState _spritePipeline;
    private readonly Dx12SectorTargetRecorder _targetRecorder;
    private readonly Dx12SectorCompositionContext[] _contexts;

    private nint _fenceEvent;
    private ulong _fenceValue;

    public Dx12SectorComposer(ID3D12Device device, Dx12TextureUploader uploader, Dx12SectorTargetPool targets, Dx12TextureUploadWorker uploads)
    {
        _device = device;
        _targets = targets;
        _sources = new Dx12SectorSourceTextureCache(uploads);
        _commandQueue = device.CreateCommandQueue(CommandListType.Direct);
        _fence = device.CreateFence(0, FenceFlags.None);
        _fenceEvent = Kernel32.CreateEventA(IntPtr.Zero, false, false, null);
        if (_fenceEvent == 0)
            throw new InvalidOperationException("Failed to create the sector-composition fence event.");

        var pipeline = Dx12SectorCompositionPipeline.Create(device, OutputFormat);
        _rootSignature = pipeline.RootSignature;
        _basePipeline = pipeline.Base;
        _coverPipeline = pipeline.Cover;
        _spritePipeline = pipeline.Sprite;
        _targetRecorder = new Dx12SectorTargetRecorder(device, uploader, _rootSignature, _spritePipeline)
        {
            OpaquePipeline = pipeline.Opaque
        };
        _contexts = new Dx12SectorCompositionContext[MaximumInFlightCompositions];
        for (var index = 0; index < _contexts.Length; index++)
            _contexts[index] = new Dx12SectorCompositionContext(device);
    }

    public bool PrepareSources(TerrainSectorComposition composition) => _sources.Prepare(composition);

    public Submission Submit(TerrainSectorComposition composition)
    {
        var context = GetAvailableContext();
        var commandList = context.CommandList;
        ID3D12Resource? baseTexture = null;
        ID3D12Resource? coverTexture = null;

        try
        {
            var maximumSourceDescriptorCount = checked((
                composition.BaseTiles.Count +
                composition.CoverTiles.Count +
                composition.EmbeddedSprites.Count) * 2);
            context.BeginRecording(maximumSourceDescriptorCount);

            if (_targets.TryRent(composition.Width, composition.Height) is { } reused)
            {
                baseTexture = reused.BaseTexture;
                coverTexture = reused.LiquidCoverTexture;
                Dx12TextureUploader.Transition(commandList, baseTexture,
                    ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
                Dx12TextureUploader.Transition(commandList, coverTexture,
                    ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            }
            else
            {
                baseTexture = CreateOutputTexture(composition.Width, composition.Height);
                coverTexture = CreateOutputTexture(composition.Width, composition.Height);
            }
            var rtvStart = context.RtvHeap.GetCPUDescriptorHandleForHeapStart();
            var rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
            var baseRtv = rtvStart;
            var coverRtv = rtvStart + rtvDescriptorSize;
            _device.CreateRenderTargetView(baseTexture, null, baseRtv);
            _device.CreateRenderTargetView(coverTexture, null, coverRtv);

            var baseDraws = CreateDraws(
                composition.BaseTiles,
                false,
                commandList);
            var coverDraws = CreateDraws(
                composition.CoverTiles,
                true,
                commandList);
            var embeddedSpriteDraws = CreateEmbeddedSpriteDraws(
                composition.EmbeddedSprites,
                commandList);

            var sourceSrvHeap = context.SourceSrvHeap;
            var nextSourceDescriptor = 0;
            _targetRecorder.RecordTarget(
                commandList,
                baseTexture,
                baseRtv,
                composition.Width,
                composition.Height,
                baseDraws,
                embeddedSpriteDraws,
                _basePipeline,
                context.TransientResources,
                sourceSrvHeap,
                ref nextSourceDescriptor);
            _targetRecorder.RecordTarget(
                commandList,
                coverTexture,
                coverRtv,
                composition.Width,
                composition.Height,
                coverDraws,
                [],
                _coverPipeline,
                context.TransientResources,
                sourceSrvHeap,
                ref nextSourceDescriptor);

            commandList.Close();
            _commandQueue.ExecuteCommandLists([commandList]);
            _sources.CommitRecordedTransitions();
            var fenceValue = ++_fenceValue;
            _commandQueue.Signal(_fence, fenceValue).CheckError();
            context.FenceValue = fenceValue;

            var submission = new Submission(
                context,
                fenceValue,
                baseTexture,
                coverTexture);
            baseTexture = null;
            coverTexture = null;
            return submission;
        }
        catch
        {
            _sources.ResetRecordedTransitions();

            throw;
        }
        finally
        {
            if (context.FenceValue == 0)
                context.ReleaseTransientResources();
            coverTexture?.Dispose();
            baseTexture?.Dispose();
        }
    }

    public bool TryComplete(Submission submission, out Dx12ComposedSector? composed)
    {
        if (_fence.CompletedValue < submission.FenceValue)
        {
            composed = null;
            return false;
        }

        composed = submission.TakeResult();
        submission.Context.ReleaseTransientResources();
        return true;
    }

    public Dx12ComposedSector Complete(Submission submission)
    {
        WaitForFence(submission.FenceValue);
        var composed = submission.TakeResult();
        submission.Context.ReleaseTransientResources();
        return composed;
    }

    public void Dispose()
    {
        WaitForFence(_fenceValue);
        foreach (var context in _contexts)
            context.Dispose();
        _sources.Dispose();

        _spritePipeline.Dispose();
        _coverPipeline.Dispose();
        _basePipeline.Dispose();
        _targetRecorder.OpaquePipeline.Dispose();
        _rootSignature.Dispose();
        _fence.Dispose();
        _commandQueue.Dispose();
        if (_fenceEvent != 0)
        {
            Kernel32.CloseHandle(_fenceEvent);
            _fenceEvent = 0;
        }
    }

    private Dx12SectorCompositionContext GetAvailableContext()
    {
        foreach (var context in _contexts)
            if (context.FenceValue == 0)
                return context;

        throw new InvalidOperationException("The sector compositor has no available command context.");
    }

    private GpuTerrainTileDraw[] CreateDraws(
        IReadOnlyList<TerrainCompositionTile> tiles,
        bool premultipliedOutput,
        ID3D12GraphicsCommandList commandList)
    {
        var draws = new GpuTerrainTileDraw[tiles.Count];
        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            var primary = _sources.Get(tile.Primary.Texture, commandList);
            var secondary = primary;
            var flags = premultipliedOutput ? PremultipliedOutputFlag : 0u;
            if (tile.Secondary is { } secondaryTile)
            {
                secondary = _sources.Get(secondaryTile.Texture, commandList);
                flags |= HasSecondaryMaskFlag;
            }

            draws[index] = new GpuTerrainTileDraw(
                new GpuTerrainTileInstance(
                    tile.ScreenX,
                    tile.ScreenY,
                    tile.Primary.SourceX,
                    tile.Primary.SourceY,
                    tile.Secondary?.SourceX ?? tile.Primary.SourceX,
                    tile.Secondary?.SourceY ?? tile.Primary.SourceY,
                    0,
                    0,
                    flags,
                    tile.Surface),
                primary,
                secondary) { IsOpaque = (tile.Secondary ?? tile.Primary).IsOpaque };
        }

        return draws;
    }

    private GpuSectorSpriteDraw[] CreateEmbeddedSpriteDraws(
        IReadOnlyList<TerrainEmbeddedSprite> sprites,
        ID3D12GraphicsCommandList commandList)
    {
        var draws = new GpuSectorSpriteDraw[sprites.Count];
        for (var index = 0; index < sprites.Count; index++)
        {
            var sprite = sprites[index];
            draws[index] = new GpuSectorSpriteDraw(
                new GpuSectorSpriteInstance(sprite),
                _sources.Get(sprite.Sprite, commandList));
        }

        return draws;
    }

    private ID3D12Resource CreateOutputTexture(int width, int height)
    {
        var description = new ResourceDescription(
            ResourceDimension.Texture2D,
            0,
            (ulong)width,
            (uint)height,
            1,
            1,
            OutputFormat,
            1,
            0,
            TextureLayout.Unknown,
            ResourceFlags.AllowRenderTarget);
        return _device.CreateCommittedResource(
            new HeapProperties(HeapType.Default, 0, 0),
            HeapFlags.None,
            description,
            ResourceStates.RenderTarget,
            null);
    }

    private void WaitForFence(ulong fenceValue)
    {
        if (fenceValue == 0 || _fence.CompletedValue >= fenceValue)
            return;

        _fence.SetEventOnCompletion(fenceValue, _fenceEvent).CheckError();
        Kernel32.WaitForSingleObject(_fenceEvent, uint.MaxValue);
    }

    internal sealed class Submission(
        Dx12SectorCompositionContext context,
        ulong fenceValue,
        ID3D12Resource baseTexture,
        ID3D12Resource liquidCoverTexture)
    {
        private ID3D12Resource? _baseTexture = baseTexture;
        private ID3D12Resource? _liquidCoverTexture = liquidCoverTexture;

        public Dx12SectorCompositionContext Context { get; } = context;
        public ulong FenceValue { get; } = fenceValue;

        public Dx12ComposedSector TakeResult()
        {
            if (_baseTexture is null ||
                _liquidCoverTexture is null)
            {
                throw new InvalidOperationException("The sector composition result was already collected.");
            }

            var result = new Dx12ComposedSector(
                _baseTexture,
                _liquidCoverTexture);
            _baseTexture = null;
            _liquidCoverTexture = null;
            return result;
        }
    }
}

internal sealed record Dx12ComposedSector(
    ID3D12Resource BaseTexture,
    ID3D12Resource LiquidCoverTexture) : IDisposable
{
    public void Dispose()
    {
        LiquidCoverTexture.Dispose();
        BaseTexture.Dispose();
    }
}
