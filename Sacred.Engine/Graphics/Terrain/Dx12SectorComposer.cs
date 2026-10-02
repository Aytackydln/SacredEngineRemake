using System;
using System.Collections.Generic;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Extern;
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

    private const int MaximumTileSheetCount = 4096;
    private const uint HasSecondaryMaskFlag = 0x01;
    private const uint PremultipliedOutputFlag = 0x02;
    private const Format OutputFormat = Format.R8G8B8A8_UNorm;

    private readonly ID3D12Device _device;
    private readonly Dx12TextureUploader _uploader;
    private readonly ID3D12CommandQueue _commandQueue;
    private readonly ID3D12Fence _fence;
    private readonly Dictionary<string, SourceTexture> _sourceTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<StaticSpriteAsset, SourceTexture> _spriteSourceTextures =
        new(ReferenceEqualityComparer.Instance);
    private readonly ID3D12RootSignature _rootSignature;
    private readonly ID3D12PipelineState _basePipeline;
    private readonly ID3D12PipelineState _coverPipeline;
    private readonly ID3D12PipelineState _spritePipeline;
    private readonly Dx12SectorTargetRecorder _targetRecorder;
    private readonly Dx12SectorCompositionContext[] _contexts;

    private nint _fenceEvent;
    private ulong _fenceValue;

    public Dx12SectorComposer(ID3D12Device device, Dx12TextureUploader uploader)
    {
        _device = device;
        _uploader = uploader;
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

    public Submission Submit(TerrainSectorComposition composition)
    {
        var context = GetAvailableContext();
        var commandList = context.CommandList;
        ID3D12Resource? baseTexture = null;
        ID3D12Resource? coverTexture = null;
        var addedSourceNames = new List<string>();
        var addedSpriteSources = new List<StaticSpriteAsset>();

        try
        {
            var maximumSourceDescriptorCount = checked((
                composition.BaseTiles.Count +
                composition.CoverTiles.Count +
                composition.EmbeddedSprites.Count) * 2);
            context.BeginRecording(maximumSourceDescriptorCount);

            baseTexture = CreateOutputTexture(composition.Width, composition.Height);
            coverTexture = CreateOutputTexture(composition.Width, composition.Height);
            var rtvStart = context.RtvHeap.GetCPUDescriptorHandleForHeapStart();
            var rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
            var baseRtv = rtvStart;
            var coverRtv = rtvStart + rtvDescriptorSize;
            _device.CreateRenderTargetView(baseTexture, null, baseRtv);
            _device.CreateRenderTargetView(coverTexture, null, coverRtv);

            var baseDraws = CreateDraws(
                composition.BaseTiles,
                false,
                commandList,
                context.TransientResources,
                addedSourceNames);
            var coverDraws = CreateDraws(
                composition.CoverTiles,
                true,
                commandList,
                context.TransientResources,
                addedSourceNames);
            var embeddedSpriteDraws = CreateEmbeddedSpriteDraws(
                composition.EmbeddedSprites,
                commandList,
                context.TransientResources,
                addedSpriteSources);

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
            // A source descriptor allocated by a failed composition must not remain discoverable.
            // Slots are intentionally not reused; this avoids aliasing any descriptor that may have
            // reached the GPU before an execution or fence failure was reported.
            foreach (var name in addedSourceNames)
            {
                if (_sourceTextures.Remove(name, out var source))
                    source.Resource.Dispose();
            }
            foreach (var sprite in addedSpriteSources)
            {
                if (_spriteSourceTextures.Remove(sprite, out var source))
                    source.Resource.Dispose();
            }

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
        foreach (var source in _sourceTextures.Values)
            source.Resource.Dispose();
        _sourceTextures.Clear();
        foreach (var source in _spriteSourceTextures.Values)
            source.Resource.Dispose();
        _spriteSourceTextures.Clear();

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
        ID3D12GraphicsCommandList commandList,
        ICollection<ID3D12Resource> transientResources,
        ICollection<string> addedSourceNames)
    {
        var draws = new GpuTerrainTileDraw[tiles.Count];
        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            var primary = EnsureSourceTexture(
                tile.Primary.Texture,
                commandList,
                transientResources,
                addedSourceNames);
            var secondary = primary;
            var flags = premultipliedOutput ? PremultipliedOutputFlag : 0u;
            if (tile.Secondary is { } secondaryTile)
            {
                secondary = EnsureSourceTexture(
                    secondaryTile.Texture,
                    commandList,
                    transientResources,
                    addedSourceNames);
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
        ID3D12GraphicsCommandList commandList,
        ICollection<ID3D12Resource> transientResources,
        ICollection<StaticSpriteAsset> addedSpriteSources)
    {
        var draws = new GpuSectorSpriteDraw[sprites.Count];
        for (var index = 0; index < sprites.Count; index++)
        {
            var sprite = sprites[index];
            draws[index] = new GpuSectorSpriteDraw(
                new GpuSectorSpriteInstance(sprite),
                EnsureSourceTexture(sprite.Sprite, commandList, transientResources, addedSpriteSources));
        }

        return draws;
    }

    private SourceTexture EnsureSourceTexture(
        TextureAsset texture,
        ID3D12GraphicsCommandList commandList,
        ICollection<ID3D12Resource> transientResources,
        ICollection<string> addedSourceNames)
    {
        if (_sourceTextures.TryGetValue(texture.Name, out var cached))
            return cached;
        if (_sourceTextures.Count >= MaximumTileSheetCount)
            throw new InvalidOperationException($"The terrain tile-sheet cache exhausted its {MaximumTileSheetCount} textures.");

        ID3D12Resource? resource = null;
        try
        {
            resource = _uploader.UploadRgbaTexture(
                commandList,
                texture.Width,
                texture.Height,
                texture.Rgba8,
                transientResources);
            var source = new SourceTexture(resource);
            _sourceTextures.Add(texture.Name, source);
            addedSourceNames.Add(texture.Name);
            return source;
        }
        catch
        {
            resource?.Dispose();
            throw;
        }
    }

    private SourceTexture EnsureSourceTexture(
        StaticSpriteAsset sprite,
        ID3D12GraphicsCommandList commandList,
        ICollection<ID3D12Resource> transientResources,
        ICollection<StaticSpriteAsset> addedSources)
    {
        if (_spriteSourceTextures.TryGetValue(sprite, out var cached))
            return cached;

        ID3D12Resource? resource = null;
        try
        {
            resource = _uploader.UploadRgbaTexture(
                commandList,
                sprite.AtlasWidth,
                sprite.AtlasHeight,
                sprite.Rgba,
                transientResources);
            var source = new SourceTexture(resource);
            _spriteSourceTextures.Add(sprite, source);
            addedSources.Add(sprite);
            // A shared sprite can be baked into the exterior and later uploaded
            // for an active elevated indoor surface.
            return source;
        }
        catch
        {
            resource?.Dispose();
            throw;
        }
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
