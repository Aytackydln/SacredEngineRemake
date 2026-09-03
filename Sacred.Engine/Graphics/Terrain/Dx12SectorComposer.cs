using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Extern;
using Sacred.Engine.Rendering;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics.Terrain;

/// <summary>
/// Rasterizes compact tile plans into persistent sector textures on a dedicated Direct3D queue.
/// Composition is serialized, so transient descriptors and command resources can be reused safely.
/// </summary>
internal sealed class Dx12SectorComposer : IDisposable
{
    private const int MaximumTileSheetCount = 4096;
    private const int VerticesPerTile = 6;
    private const uint HasSecondaryMaskFlag = 0x01;
    private const uint PremultipliedOutputFlag = 0x02;
    private const Format OutputFormat = Format.R8G8B8A8_UNorm;

    private readonly ID3D12Device _device;
    private readonly Dx12TextureUploader _uploader;
    private readonly ID3D12CommandQueue _commandQueue;
    private readonly ID3D12Fence _fence;
    private readonly Dictionary<string, SourceTexture> _sourceTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ID3D12RootSignature _rootSignature;
    private readonly ID3D12PipelineState _basePipeline;
    private readonly ID3D12PipelineState _coverPipeline;

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
    }

    public Dx12ComposedSector Compose(TerrainSectorComposition composition)
    {
        ID3D12CommandAllocator? commandAllocator = null;
        ID3D12GraphicsCommandList? commandList = null;
        ID3D12DescriptorHeap? rtvHeap = null;
        ID3D12DescriptorHeap? sourceSrvHeap = null;
        ID3D12Resource? baseTexture = null;
        ID3D12Resource? coverTexture = null;
        ID3D12Resource? stairsDebugTexture = null;
        ID3D12Resource? blockedAreaDebugTexture = null;
        ID3D12Resource? terrainTopologyDebugTexture = null;
        var transientResources = new List<ID3D12Resource>();
        var addedSourceNames = new List<string>();

        try
        {
            commandAllocator = _device.CreateCommandAllocator(CommandListType.Direct);
            commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(
                CommandListType.Direct,
                commandAllocator,
                null);

            baseTexture = CreateOutputTexture(composition.Width, composition.Height);
            coverTexture = CreateOutputTexture(composition.Width, composition.Height);
            stairsDebugTexture = CreateOutputTexture(
                composition.StairsDebugWidth,
                composition.StairsDebugHeight);
            blockedAreaDebugTexture = CreateOutputTexture(
                composition.BlockedAreaDebugWidth,
                composition.BlockedAreaDebugHeight);
            terrainTopologyDebugTexture = CreateOutputTexture(
                composition.TerrainTopologyDebugWidth,
                composition.TerrainTopologyDebugHeight);
            rtvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(
                DescriptorHeapType.RenderTargetView,
                5,
                DescriptorHeapFlags.None,
                0));
            var rtvStart = rtvHeap.GetCPUDescriptorHandleForHeapStart();
            var rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
            var baseRtv = rtvStart;
            var coverRtv = rtvStart + rtvDescriptorSize;
            var stairsDebugRtv = rtvStart + rtvDescriptorSize * 2;
            var blockedAreaDebugRtv = rtvStart + rtvDescriptorSize * 3;
            var terrainTopologyDebugRtv = rtvStart + rtvDescriptorSize * 4;
            _device.CreateRenderTargetView(baseTexture, null, baseRtv);
            _device.CreateRenderTargetView(coverTexture, null, coverRtv);
            _device.CreateRenderTargetView(stairsDebugTexture, null, stairsDebugRtv);
            _device.CreateRenderTargetView(blockedAreaDebugTexture, null, blockedAreaDebugRtv);
            _device.CreateRenderTargetView(terrainTopologyDebugTexture, null, terrainTopologyDebugRtv);

            var baseDraws = CreateDraws(
                composition.BaseTiles,
                false,
                commandList,
                transientResources,
                addedSourceNames);
            var coverDraws = CreateDraws(
                composition.CoverTiles,
                true,
                commandList,
                transientResources,
                addedSourceNames);
            var stairsDebugDraws = CreateDraws(
                composition.StairsDebugTiles,
                true,
                commandList,
                transientResources,
                addedSourceNames);
            var blockedAreaDebugDraws = CreateDraws(
                composition.BlockedAreaDebugTiles,
                true,
                commandList,
                transientResources,
                addedSourceNames);
            var terrainTopologyDebugDraws = CreateDraws(
                composition.TerrainTopologyDebugTiles,
                true,
                commandList,
                transientResources,
                addedSourceNames);

            sourceSrvHeap = CreateSourceDescriptorHeap(
                baseDraws.Length + coverDraws.Length + stairsDebugDraws.Length +
                blockedAreaDebugDraws.Length + terrainTopologyDebugDraws.Length);
            var nextSourceDescriptor = 0;
            RecordTarget(
                commandList,
                baseTexture,
                baseRtv,
                composition.Width,
                composition.Height,
                baseDraws,
                _basePipeline,
                transientResources,
                sourceSrvHeap,
                ref nextSourceDescriptor);
            RecordTarget(
                commandList,
                blockedAreaDebugTexture,
                blockedAreaDebugRtv,
                composition.BlockedAreaDebugWidth,
                composition.BlockedAreaDebugHeight,
                blockedAreaDebugDraws,
                _coverPipeline,
                transientResources,
                sourceSrvHeap,
                ref nextSourceDescriptor);
            RecordTarget(
                commandList,
                stairsDebugTexture,
                stairsDebugRtv,
                composition.StairsDebugWidth,
                composition.StairsDebugHeight,
                stairsDebugDraws,
                _coverPipeline,
                transientResources,
                sourceSrvHeap,
                ref nextSourceDescriptor);
            RecordTarget(
                commandList,
                terrainTopologyDebugTexture,
                terrainTopologyDebugRtv,
                composition.TerrainTopologyDebugWidth,
                composition.TerrainTopologyDebugHeight,
                terrainTopologyDebugDraws,
                _coverPipeline,
                transientResources,
                sourceSrvHeap,
                ref nextSourceDescriptor);
            RecordTarget(
                commandList,
                coverTexture,
                coverRtv,
                composition.Width,
                composition.Height,
                coverDraws,
                _coverPipeline,
                transientResources,
                sourceSrvHeap,
                ref nextSourceDescriptor);

            commandList.Close();
            _commandQueue.ExecuteCommandLists([commandList]);
            var fenceValue = ++_fenceValue;
            _commandQueue.Signal(_fence, fenceValue).CheckError();
            WaitForFence(fenceValue);

            foreach (var resource in transientResources)
                resource.Dispose();
            transientResources.Clear();
            commandList.Dispose();
            commandList = null;
            commandAllocator.Dispose();
            commandAllocator = null;
            rtvHeap.Dispose();
            rtvHeap = null;
            sourceSrvHeap.Dispose();
            sourceSrvHeap = null;

            var result = new Dx12ComposedSector(
                baseTexture,
                coverTexture,
                stairsDebugTexture,
                blockedAreaDebugTexture,
                terrainTopologyDebugTexture);
            baseTexture = null;
            coverTexture = null;
            stairsDebugTexture = null;
            blockedAreaDebugTexture = null;
            terrainTopologyDebugTexture = null;
            return result;
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

            throw;
        }
        finally
        {
            foreach (var resource in transientResources)
                resource.Dispose();
            blockedAreaDebugTexture?.Dispose();
            terrainTopologyDebugTexture?.Dispose();
            stairsDebugTexture?.Dispose();
            coverTexture?.Dispose();
            baseTexture?.Dispose();
            rtvHeap?.Dispose();
            sourceSrvHeap?.Dispose();
            commandList?.Dispose();
            commandAllocator?.Dispose();
        }
    }

    public void Dispose()
    {
        WaitForFence(_fenceValue);
        foreach (var source in _sourceTextures.Values)
            source.Resource.Dispose();
        _sourceTextures.Clear();

        _coverPipeline.Dispose();
        _basePipeline.Dispose();
        _rootSignature.Dispose();
        _fence.Dispose();
        _commandQueue.Dispose();
        if (_fenceEvent != 0)
        {
            Kernel32.CloseHandle(_fenceEvent);
            _fenceEvent = 0;
        }
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
                secondary);
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

    private unsafe void RecordTarget(
        ID3D12GraphicsCommandList commandList,
        ID3D12Resource target,
        CpuDescriptorHandle rtv,
        int width,
        int height,
        GpuTerrainTileDraw[] draws,
        ID3D12PipelineState pipeline,
        ICollection<ID3D12Resource> transientResources,
        ID3D12DescriptorHeap sourceSrvHeap,
        ref int nextSourceDescriptor)
    {
        commandList.OMSetRenderTargets(rtv, null);
        commandList.ClearRenderTargetView(rtv, new Color4(0.0f, 0.0f, 0.0f, 0.0f));
        commandList.RSSetViewports(new Viewport(0, 0, width, height, 0.0f, 1.0f));
        commandList.RSSetScissorRects(new RawRect(0, 0, width, height));

        if (draws.Length != 0)
        {
            var instances = new GpuTerrainTileInstance[draws.Length];
            for (var index = 0; index < draws.Length; index++)
                instances[index] = draws[index].Instance;
            var instanceBytes = MemoryMarshal.AsBytes(instances.AsSpan());
            var instanceBuffer = _uploader.CreateUploadBuffer(instanceBytes);
            transientResources.Add(instanceBuffer);
            commandList.SetDescriptorHeaps(1, [sourceSrvHeap]);
            commandList.SetGraphicsRootSignature(_rootSignature);
            commandList.SetPipelineState(pipeline);
            commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            var targetSize = stackalloc float[2] { width, height };
            commandList.SetGraphicsRoot32BitConstants(0, 2, targetSize, 0);
            var sourceCpuStart = sourceSrvHeap.GetCPUDescriptorHandleForHeapStart();
            var sourceGpuStart = sourceSrvHeap.GetGPUDescriptorHandleForHeapStart();
            var sourceDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(
                DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            var instanceStride = Marshal.SizeOf<GpuTerrainTileInstance>();
            var firstInstance = 0;
            while (firstInstance < draws.Length)
            {
                var draw = draws[firstInstance];
                var instanceCount = 1;
                while (firstInstance + instanceCount < draws.Length &&
                       ReferenceEquals(draw.Primary, draws[firstInstance + instanceCount].Primary) &&
                       ReferenceEquals(draw.Secondary, draws[firstInstance + instanceCount].Secondary))
                    instanceCount++;

                var primaryDescriptor = sourceCpuStart + nextSourceDescriptor * sourceDescriptorSize;
                _uploader.CreateShaderResourceView(draw.Primary.Resource, primaryDescriptor);
                _uploader.CreateShaderResourceView(draw.Secondary.Resource, primaryDescriptor + sourceDescriptorSize);
                commandList.SetGraphicsRootDescriptorTable(
                    2,
                    sourceGpuStart + nextSourceDescriptor * sourceDescriptorSize);
                commandList.SetGraphicsRootShaderResourceView(
                    1,
                    instanceBuffer.GPUVirtualAddress + (ulong)(firstInstance * instanceStride));
                commandList.DrawInstanced(VerticesPerTile, (uint)instanceCount, 0, 0);
                nextSourceDescriptor += 2;
                firstInstance += instanceCount;
            }
        }

        Dx12TextureUploader.Transition(
            commandList,
            target,
            ResourceStates.RenderTarget,
            ResourceStates.PixelShaderResource);
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

    private ID3D12DescriptorHeap CreateSourceDescriptorHeap(int drawCount) =>
        _device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            checked((uint)Math.Max(2, drawCount * 2)),
            DescriptorHeapFlags.ShaderVisible,
            0));

    private sealed record SourceTexture(ID3D12Resource Resource);

    private readonly record struct GpuTerrainTileDraw(
        GpuTerrainTileInstance Instance,
        SourceTexture Primary,
        SourceTexture Secondary);
}

internal sealed record Dx12ComposedSector(
    ID3D12Resource BaseTexture,
    ID3D12Resource LiquidCoverTexture,
    ID3D12Resource StairsDebugTexture,
    ID3D12Resource BlockedAreaDebugTexture,
    ID3D12Resource TerrainTopologyDebugTexture) : IDisposable
{
    public void Dispose()
    {
        BlockedAreaDebugTexture.Dispose();
        TerrainTopologyDebugTexture.Dispose();
        StairsDebugTexture.Dispose();
        LiquidCoverTexture.Dispose();
        BaseTexture.Dispose();
    }
}
