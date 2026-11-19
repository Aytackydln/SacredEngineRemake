using System;
using Vortice.D3D12MemoryAllocator;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Memory;

/// <summary>Device-wide GPU memory service backed by D3D12MemoryAllocator. Resources are suballocated from
/// pooled heaps (placed resources) instead of each getting its own committed allocation, so creating and
/// destroying them does not hit the OS video-memory allocator. Allocate at load or growth time, never
/// per frame: per-frame data belongs in <see cref="Dx12FrameUploadArena"/>.
/// Every <see cref="Dx12GpuResource"/> must be disposed before this allocator is.</summary>
internal sealed class Dx12MemoryAllocator : IDisposable
{
    private readonly Allocator _allocator;
    private uint _frameIndex;

    public Dx12MemoryAllocator(ID3D12Device device, IDXGIAdapter adapter)
    {
        // Small buffers are placed too (the default prefers committed ones), and the default pools are zero-filled
        // when a heap block is created so a fresh texture never exposes stale memory.
        _allocator = D3D12MA.CreateAllocator(new AllocatorDescription(
            device, adapter, AllocatorFlags.DontPreferSmallBuffersCommitted));
    }

    /// <summary>Lets the allocator refresh its video-memory budget once per frame.</summary>
    public void BeginFrame() => _allocator.SetCurrentFrameIndex(++_frameIndex);

    public Dx12GpuResource CreateBuffer(HeapType heapType, ulong sizeBytes,
        ResourceFlags flags = ResourceFlags.None, ResourceStates? initialState = null) =>
        Create(heapType, ResourceDescription.Buffer(sizeBytes, flags), initialState ?? DefaultState(heapType));

    public Dx12GpuResource Create(HeapType heapType, in ResourceDescription description,
        ResourceStates initialState, ClearValue? clearValue = null)
    {
        var allocation = _allocator.CreateResource(
            new AllocationDescription(heapType, AllocationFlags.None), description, initialState,
            out ID3D12Resource resource, clearValue);
        return new Dx12GpuResource(allocation, resource);
    }

    /// <summary>Heap and block counts for diagnostics (committed memory versus bytes handed out).</summary>
    public Dx12MemoryStatistics Statistics()
    {
        var total = _allocator.CalculateStatistics().Total.Statistics;
        return new Dx12MemoryStatistics(total.BlockCount, total.AllocationCount, total.BlockBytes, total.AllocationBytes);
    }

    private static ResourceStates DefaultState(HeapType heapType) => heapType switch
    {
        HeapType.Upload => ResourceStates.GenericRead,
        HeapType.Readback => ResourceStates.CopyDest,
        _ => ResourceStates.Common
    };

    public void Dispose() => _allocator.Dispose();
}

internal readonly record struct Dx12MemoryStatistics(uint HeapBlocks, uint Allocations, ulong BlockBytes, ulong AllocationBytes);

/// <summary>A resource plus the allocator block it lives in. Disposing releases both; call only once the GPU is done with it.</summary>
internal sealed class Dx12GpuResource : IDisposable
{
    private readonly Allocation _allocation;
    private nint _mapped;

    public Dx12GpuResource(Allocation allocation, ID3D12Resource resource)
    {
        _allocation = allocation;
        Resource = resource;
    }

    public ID3D12Resource Resource { get; }
    public ulong SizeBytes => _allocation.Size;
    public ulong GpuAddress => Resource.GPUVirtualAddress;

    /// <summary>Maps an upload/readback buffer once and keeps it mapped for the resource's lifetime.</summary>
    public unsafe nint Map()
    {
        if (_mapped != 0)
            return _mapped;
        void* pointer;
        Resource.Map(0, null, &pointer).CheckError();
        return _mapped = (nint)pointer;
    }

    public void Dispose()
    {
        if (_mapped != 0)
        {
            Resource.Unmap(0, null);
            _mapped = 0;
        }
        Resource.Dispose();
        _allocation.Dispose();
    }
}
