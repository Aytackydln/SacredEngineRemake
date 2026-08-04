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
    private const int VerticesPerTile = 12;
    private const uint HasSecondaryMaskFlag = 0x01;
    private const uint PremultipliedOutputFlag = 0x02;
    private const Format OutputFormat = Format.R8G8B8A8_UNorm;

    private readonly ID3D12Device _device;
    private readonly Dx12TextureUploader _uploader;
    private readonly ID3D12CommandQueue _commandQueue;
    private readonly ID3D12Fence _fence;
    private readonly ID3D12DescriptorHeap _sourceSrvHeap;
    private readonly CpuDescriptorHandle _sourceSrvCpuStart;
    private readonly GpuDescriptorHandle _sourceSrvGpuStart;
    private readonly int _sourceDescriptorSize;
    private readonly Dictionary<string, SourceTexture> _sourceTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ID3D12RootSignature _rootSignature;
    private readonly ID3D12PipelineState _basePipeline;
    private readonly ID3D12PipelineState _coverPipeline;

    private nint _fenceEvent;
    private ulong _fenceValue;
    private int _nextSourceSlot;

    public Dx12SectorComposer(ID3D12Device device, Dx12TextureUploader uploader)
    {
        _device = device;
        _uploader = uploader;
        _commandQueue = device.CreateCommandQueue(CommandListType.Direct);
        _fence = device.CreateFence(0, FenceFlags.None);
        _fenceEvent = Kernel32.CreateEventA(IntPtr.Zero, false, false, null);
        if (_fenceEvent == 0)
            throw new InvalidOperationException("Failed to create the sector-composition fence event.");

        _sourceSrvHeap = device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            MaximumTileSheetCount,
            DescriptorHeapFlags.ShaderVisible,
            0));
        _sourceSrvCpuStart = _sourceSrvHeap.GetCPUDescriptorHandleForHeapStart();
        _sourceSrvGpuStart = _sourceSrvHeap.GetGPUDescriptorHandleForHeapStart();
        _sourceDescriptorSize = (int)device.GetDescriptorHandleIncrementSize(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);

        var pipeline = Dx12SectorCompositionPipeline.Create(device, MaximumTileSheetCount, OutputFormat);
        _rootSignature = pipeline.RootSignature;
        _basePipeline = pipeline.Base;
        _coverPipeline = pipeline.Cover;
    }

    public Dx12ComposedSector Compose(TerrainSectorComposition composition)
    {
        ID3D12CommandAllocator? commandAllocator = null;
        ID3D12GraphicsCommandList? commandList = null;
        ID3D12DescriptorHeap? rtvHeap = null;
        ID3D12Resource? baseTexture = null;
        ID3D12Resource? coverTexture = null;
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
            rtvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(
                DescriptorHeapType.RenderTargetView,
                2,
                DescriptorHeapFlags.None,
                0));
            var rtvStart = rtvHeap.GetCPUDescriptorHandleForHeapStart();
            var rtvDescriptorSize = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
            var baseRtv = rtvStart;
            var coverRtv = rtvStart + rtvDescriptorSize;
            _device.CreateRenderTargetView(baseTexture, null, baseRtv);
            _device.CreateRenderTargetView(coverTexture, null, coverRtv);

            var baseInstances = CreateInstances(
                composition.BaseTiles,
                false,
                commandList,
                transientResources,
                addedSourceNames);
            var coverInstances = CreateInstances(
                composition.CoverTiles,
                true,
                commandList,
                transientResources,
                addedSourceNames);

            RecordTarget(
                commandList,
                baseTexture,
                baseRtv,
                composition.Width,
                composition.Height,
                baseInstances,
                composition.BaseTiles.Count,
                _basePipeline,
                transientResources);
            RecordTarget(
                commandList,
                coverTexture,
                coverRtv,
                composition.Width,
                composition.Height,
                coverInstances,
                composition.CoverTiles.Count,
                _coverPipeline,
                transientResources);

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

            var result = new Dx12ComposedSector(baseTexture, coverTexture);
            baseTexture = null;
            coverTexture = null;
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
            coverTexture?.Dispose();
            baseTexture?.Dispose();
            rtvHeap?.Dispose();
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
        _sourceSrvHeap.Dispose();
        _fence.Dispose();
        _commandQueue.Dispose();
        if (_fenceEvent != 0)
        {
            Kernel32.CloseHandle(_fenceEvent);
            _fenceEvent = 0;
        }
    }

    private GpuTerrainTileInstance[] CreateInstances(
        IReadOnlyList<TerrainCompositionTile> tiles,
        bool premultipliedOutput,
        ID3D12GraphicsCommandList commandList,
        ICollection<ID3D12Resource> transientResources,
        ICollection<string> addedSourceNames)
    {
        var instances = new GpuTerrainTileInstance[tiles.Count];
        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            var primarySlot = EnsureSourceTexture(
                tile.Primary.Texture,
                commandList,
                transientResources,
                addedSourceNames);
            var secondarySlot = primarySlot;
            var flags = premultipliedOutput ? PremultipliedOutputFlag : 0u;
            if (tile.Secondary is { } secondary)
            {
                secondarySlot = EnsureSourceTexture(
                    secondary.Texture,
                    commandList,
                    transientResources,
                    addedSourceNames);
                flags |= HasSecondaryMaskFlag;
            }

            instances[index] = new GpuTerrainTileInstance(
                tile.ScreenX,
                tile.ScreenY,
                tile.Primary.SourceX,
                tile.Primary.SourceY,
                tile.Secondary?.SourceX ?? tile.Primary.SourceX,
                tile.Secondary?.SourceY ?? tile.Primary.SourceY,
                (uint)primarySlot,
                (uint)secondarySlot,
                flags);
        }

        return instances;
    }

    private int EnsureSourceTexture(
        TextureAsset texture,
        ID3D12GraphicsCommandList commandList,
        ICollection<ID3D12Resource> transientResources,
        ICollection<string> addedSourceNames)
    {
        if (_sourceTextures.TryGetValue(texture.Name, out var cached))
            return cached.Slot;
        if (_nextSourceSlot >= MaximumTileSheetCount)
            throw new InvalidOperationException($"The terrain tile-sheet cache exhausted its {MaximumTileSheetCount} descriptors.");

        var slot = _nextSourceSlot++;
        ID3D12Resource? resource = null;
        try
        {
            resource = _uploader.UploadRgbaTexture(
                commandList,
                texture.Width,
                texture.Height,
                texture.Rgba8,
                transientResources);
            _uploader.CreateShaderResourceView(resource, SourceSrvCpuHandle(slot));
            _sourceTextures.Add(texture.Name, new SourceTexture(resource, slot));
            addedSourceNames.Add(texture.Name);
            return slot;
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
        GpuTerrainTileInstance[] instances,
        int instanceCount,
        ID3D12PipelineState pipeline,
        ICollection<ID3D12Resource> transientResources)
    {
        commandList.OMSetRenderTargets(rtv, null);
        commandList.ClearRenderTargetView(rtv, new Color4(0.0f, 0.0f, 0.0f, 0.0f));
        commandList.RSSetViewports(new Viewport(0, 0, width, height, 0.0f, 1.0f));
        commandList.RSSetScissorRects(new RawRect(0, 0, width, height));

        if (instanceCount != 0)
        {
            var instanceBytes = MemoryMarshal.AsBytes(instances.AsSpan());
            var instanceBuffer = _uploader.CreateUploadBuffer(instanceBytes);
            transientResources.Add(instanceBuffer);
            commandList.SetDescriptorHeaps(1, [_sourceSrvHeap]);
            commandList.SetGraphicsRootSignature(_rootSignature);
            commandList.SetPipelineState(pipeline);
            commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            var targetSize = stackalloc float[2] { width, height };
            commandList.SetGraphicsRoot32BitConstants(0, 2, targetSize, 0);
            commandList.SetGraphicsRootShaderResourceView(1, instanceBuffer.GPUVirtualAddress);
            commandList.SetGraphicsRootDescriptorTable(2, _sourceSrvGpuStart);
            commandList.DrawInstanced(VerticesPerTile, (uint)instanceCount, 0, 0);
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

    private CpuDescriptorHandle SourceSrvCpuHandle(int slot) =>
        _sourceSrvCpuStart + slot * _sourceDescriptorSize;

    private sealed record SourceTexture(ID3D12Resource Resource, int Slot);
}

internal sealed record Dx12ComposedSector(ID3D12Resource BaseTexture, ID3D12Resource LiquidCoverTexture) : IDisposable
{
    public void Dispose()
    {
        LiquidCoverTexture.Dispose();
        BaseTexture.Dispose();
    }
}
