using System;
using System.Collections.Generic;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Memory;

/// <summary>A CPU-writable slice of the frame's upload memory. Valid until the frame slot is reused.</summary>
internal readonly unsafe struct Dx12UploadAllocation
{
    private readonly nint _cpuAddress;

    public Dx12UploadAllocation(ID3D12Resource resource, ulong offset, nint cpuAddress, int sizeBytes)
    {
        Resource = resource;
        Offset = offset;
        _cpuAddress = cpuAddress;
        SizeBytes = sizeBytes;
    }

    public ID3D12Resource Resource { get; }
    public ulong Offset { get; }
    public int SizeBytes { get; }
    public ulong GpuAddress => Resource.GPUVirtualAddress + Offset;
    public Span<byte> AsSpan() => new((void*)_cpuAddress, SizeBytes);
}

/// <summary>Per-frame-slot upload memory: one persistently mapped buffer that is bump-allocated during the frame
/// and rewound once the frame's fence retires. Steady state makes no allocator calls at all. If a frame needs more
/// than the buffer holds, the overflow is served from extra chunks (rare, one-off) and the next reset folds
/// everything into a single buffer sized for the observed peak, so the arena converges instead of growing forever.
/// Not thread-safe: use from the thread that records the frame.</summary>
internal sealed class Dx12FrameUploadArena : IDisposable
{
    private const ulong InitialBytes = 4UL * 1024 * 1024;
    /// <summary>Satisfies constant/structured/raw buffer view alignment for anything bound at an allocation's address.</summary>
    private const ulong Alignment = 256;

    private readonly Dx12MemoryAllocator _memory;
    private readonly List<Chunk> _overflow = [];
    private Chunk _main;
    private ulong _peakBytes;

    public Dx12FrameUploadArena(Dx12MemoryAllocator memory)
    {
        _memory = memory;
        _main = new Chunk(memory.CreateBuffer(HeapType.Upload, InitialBytes));
    }

    /// <summary>Bytes handed out since the last reset, including alignment padding.</summary>
    public ulong UsedBytes => _peakBytes;
    public ulong CapacityBytes => _main.Capacity;

    public Dx12UploadAllocation Allocate(ReadOnlySpan<byte> data)
    {
        var allocation = Allocate(data.Length);
        data.CopyTo(allocation.AsSpan());
        return allocation;
    }

    /// <summary>Reserves <paramref name="sizeBytes"/> for the caller to fill through <see cref="Dx12UploadAllocation.AsSpan"/>.</summary>
    public Dx12UploadAllocation Allocate(int sizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        var size = Math.Max((ulong)sizeBytes, 16UL); // A zero-length range still needs a valid, distinct address.
        _peakBytes += AlignUp(size);
        if (_main.TryAllocate(size, out var offset))
            return _main.Slice(offset, sizeBytes);
        foreach (var chunk in _overflow)
            if (chunk.TryAllocate(size, out offset))
                return chunk.Slice(offset, sizeBytes);

        var extra = new Chunk(_memory.CreateBuffer(HeapType.Upload, RoundUpToPowerOfTwo(Math.Max(size, _main.Capacity))));
        _overflow.Add(extra);
        extra.TryAllocate(size, out offset);
        return extra.Slice(offset, sizeBytes);
    }

    /// <summary>Rewinds the arena. Call only after the GPU has finished with everything allocated from it.</summary>
    public void Reset()
    {
        if (_overflow.Count > 0)
        {
            var target = RoundUpToPowerOfTwo(Math.Max(_peakBytes, _main.Capacity));
            foreach (var chunk in _overflow)
                chunk.Dispose();
            _overflow.Clear();
            _main.Dispose();
            _main = new Chunk(_memory.CreateBuffer(HeapType.Upload, target));
        }
        _main.Reset();
        _peakBytes = 0;
    }

    public void Dispose()
    {
        foreach (var chunk in _overflow)
            chunk.Dispose();
        _overflow.Clear();
        _main.Dispose();
    }

    private static ulong AlignUp(ulong value) => (value + Alignment - 1) & ~(Alignment - 1);

    private static ulong RoundUpToPowerOfTwo(ulong value)
    {
        var result = InitialBytes;
        while (result < value)
            result <<= 1;
        return result;
    }

    private sealed class Chunk : IDisposable
    {
        private readonly Dx12GpuResource _resource;
        private readonly nint _mapped;
        private ulong _used;

        public Chunk(Dx12GpuResource resource)
        {
            _resource = resource;
            _mapped = resource.Map();
            Capacity = resource.SizeBytes;
        }

        public ulong Capacity { get; }

        public bool TryAllocate(ulong size, out ulong offset)
        {
            offset = AlignUp(_used);
            if (offset + size > Capacity)
                return false;
            _used = offset + size;
            return true;
        }

        public Dx12UploadAllocation Slice(ulong offset, int sizeBytes) =>
            new(_resource.Resource, offset, _mapped + (nint)offset, sizeBytes);

        public void Reset() => _used = 0;
        public void Dispose() => _resource.Dispose();
    }
}
