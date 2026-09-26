using System;
using System.Collections.Generic;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Terrain;

/// <summary>Reusable command and descriptor resources for one in-flight sector composition.</summary>
internal sealed class Dx12SectorCompositionContext : IDisposable
{
    private readonly ID3D12Device _device;
    private bool _hasRecordedCommands;
    private int _sourceDescriptorCapacity;

    public Dx12SectorCompositionContext(ID3D12Device device)
    {
        _device = device;
        CommandAllocator = device.CreateCommandAllocator(CommandListType.Direct);
        CommandList = device.CreateCommandList<ID3D12GraphicsCommandList>(
            CommandListType.Direct,
            CommandAllocator,
            null);
        RtvHeap = device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.RenderTargetView,
            5,
            DescriptorHeapFlags.None,
            0));
    }

    public ID3D12CommandAllocator CommandAllocator { get; }
    public ID3D12GraphicsCommandList CommandList { get; }
    public ID3D12DescriptorHeap RtvHeap { get; }
    public ID3D12DescriptorHeap SourceSrvHeap { get; private set; } = null!;
    public List<ID3D12Resource> TransientResources { get; } = [];
    public ulong FenceValue { get; set; }

    public void BeginRecording(int sourceDescriptorCount)
    {
        if (_hasRecordedCommands)
        {
            CommandAllocator.Reset();
            CommandList.Reset(CommandAllocator, null);
        }
        else
        {
            _hasRecordedCommands = true;
        }

        EnsureSourceDescriptorCapacity(sourceDescriptorCount);
    }

    public void ReleaseTransientResources()
    {
        foreach (var resource in TransientResources)
            resource.Dispose();
        TransientResources.Clear();
        FenceValue = 0;
    }

    public void Dispose()
    {
        ReleaseTransientResources();
        SourceSrvHeap?.Dispose();
        RtvHeap.Dispose();
        CommandList.Dispose();
        CommandAllocator.Dispose();
    }

    private void EnsureSourceDescriptorCapacity(int requestedCount)
    {
        requestedCount = Math.Max(2, requestedCount);
        if (_sourceDescriptorCapacity >= requestedCount)
            return;

        SourceSrvHeap?.Dispose();
        SourceSrvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            checked((uint)requestedCount),
            DescriptorHeapFlags.ShaderVisible,
            0));
        _sourceDescriptorCapacity = requestedCount;
    }
}
