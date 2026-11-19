using System;
using System.Collections.Generic;
using Sacred.Engine.Graphics.Memory;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Frames;

/// <summary>
/// Owns CPU-writable and allocator state that cannot be reused until one swap-chain frame retires.
/// </summary>
internal sealed class Dx12FrameContext : IDisposable
{
    private readonly List<ID3D12Resource> _retiredResources = new(64);
    private readonly List<int> _retiredSectorSrvSlots = new(8);
    private readonly List<int> _retiredModelSrvSlots = new(8);
    private readonly List<Action> _retirementActions = new(8);

    private readonly Dx12MemoryAllocator _memory;
    private Dx12GpuResource? _spriteInstanceBuffer;
    private nint _spriteInstanceBufferMapped;
    private int _spriteInstanceCapacity;
    private Dx12GpuResource? _staticShadowInstanceBuffer;
    private nint _staticShadowInstanceBufferMapped;
    private int _staticShadowInstanceCapacity;
    private Dx12GpuResource? _lightHaloInstanceBuffer;
    private nint _lightHaloInstanceBufferMapped;
    private int _lightHaloInstanceCapacity;

    public Dx12FrameContext(int index, ID3D12CommandAllocator commandAllocator, Dx12MemoryAllocator memory)
    {
        Index = index;
        CommandAllocator = commandAllocator;
        _memory = memory;
        Uploads = new Dx12FrameUploadArena(memory);
    }

    /// <summary>Transient per-frame upload memory. Rewound when this frame slot retires; never create a buffer per frame instead.</summary>
    public Dx12FrameUploadArena Uploads { get; }

    public int Index { get; }
    public ID3D12CommandAllocator CommandAllocator { get; }
    public ulong FenceValue { get; set; }
    public List<ID3D12Resource> TransientResources => _retiredResources;
    public ID3D12Resource SpriteInstanceBuffer =>
        _spriteInstanceBuffer?.Resource ?? throw new InvalidOperationException("The sprite instance buffer has not been created.");
    public nint SpriteInstanceBufferMapped => _spriteInstanceBufferMapped;
    public ID3D12Resource StaticShadowInstanceBuffer =>
        _staticShadowInstanceBuffer?.Resource ??
        throw new InvalidOperationException("The static-shadow instance buffer has not been created.");
    public nint StaticShadowInstanceBufferMapped => _staticShadowInstanceBufferMapped;
    public ID3D12Resource LightHaloInstanceBuffer =>
        _lightHaloInstanceBuffer?.Resource ?? throw new InvalidOperationException("The light-halo instance buffer has not been created.");
    public nint LightHaloInstanceBufferMapped => _lightHaloInstanceBufferMapped;

    public void RetireResource(ID3D12Resource resource) => _retiredResources.Add(resource);

    public void RetireAfterFence(Action release) => _retirementActions.Add(release);

    public void RetireSectorSrvSlot(int slot) => _retiredSectorSrvSlots.Add(slot);

    public void RetireModelSrvSlot(int slot) => _retiredModelSrvSlots.Add(slot);

    public int ReleaseRetiredResources(Stack<int> freeSectorSrvSlots, Stack<int> freeModelSrvSlots)
    {
        ReleaseRetirementActions();
        foreach (var resource in _retiredResources)
            resource.Dispose();
        _retiredResources.Clear();
        Uploads.Reset();

        var releasedSectorSlotCount = _retiredSectorSrvSlots.Count;
        foreach (var slot in _retiredSectorSrvSlots)
            freeSectorSrvSlots.Push(slot);
        _retiredSectorSrvSlots.Clear();

        foreach (var slot in _retiredModelSrvSlots)
            freeModelSrvSlots.Push(slot);
        _retiredModelSrvSlots.Clear();

        return releasedSectorSlotCount;
    }

    public unsafe void EnsureSpriteInstanceCapacity(
        int instanceStride,
        int requiredCapacity)
    {
        EnsureInstanceCapacity(
            instanceStride,
            requiredCapacity,
            ref _spriteInstanceBuffer,
            ref _spriteInstanceBufferMapped,
            ref _spriteInstanceCapacity);
    }

    public unsafe void EnsureLightHaloInstanceCapacity(
        int instanceStride,
        int requiredCapacity)
    {
        EnsureInstanceCapacity(
            instanceStride,
            requiredCapacity,
            ref _lightHaloInstanceBuffer,
            ref _lightHaloInstanceBufferMapped,
            ref _lightHaloInstanceCapacity);
    }

    public unsafe void EnsureStaticShadowInstanceCapacity(
        int instanceStride,
        int requiredCapacity)
    {
        EnsureInstanceCapacity(
            instanceStride,
            requiredCapacity,
            ref _staticShadowInstanceBuffer,
            ref _staticShadowInstanceBufferMapped,
            ref _staticShadowInstanceCapacity);
    }

    private void EnsureInstanceCapacity(
        int instanceStride,
        int requiredCapacity,
        ref Dx12GpuResource? buffer,
        ref nint mappedAddress,
        ref int capacity)
    {
        if (buffer is not null && capacity >= requiredCapacity)
            return;

        // Growth only: capacity doubles, so this runs a handful of times per run, from pooled heap memory.
        DisposeInstanceBuffer(ref buffer, ref mappedAddress, ref capacity);

        capacity = Math.Max(256, RoundUpToPowerOfTwo(requiredCapacity));
        buffer = _memory.CreateBuffer(HeapType.Upload, checked((ulong)capacity * (ulong)instanceStride));
        mappedAddress = buffer.Map();
    }

    public void Dispose()
    {
        ReleaseRetirementActions();
        DisposeSpriteInstanceBuffer();
        DisposeInstanceBuffer(
            ref _staticShadowInstanceBuffer,
            ref _staticShadowInstanceBufferMapped,
            ref _staticShadowInstanceCapacity);
        DisposeInstanceBuffer(
            ref _lightHaloInstanceBuffer,
            ref _lightHaloInstanceBufferMapped,
            ref _lightHaloInstanceCapacity);

        foreach (var resource in _retiredResources)
            resource.Dispose();
        _retiredResources.Clear();

        Uploads.Dispose();
        CommandAllocator.Dispose();
    }

    private void ReleaseRetirementActions()
    {
        foreach (var release in _retirementActions) release();
        _retirementActions.Clear();
    }

    private void DisposeSpriteInstanceBuffer()
    {
        DisposeInstanceBuffer(
            ref _spriteInstanceBuffer,
            ref _spriteInstanceBufferMapped,
            ref _spriteInstanceCapacity);
    }

    private static void DisposeInstanceBuffer(
        ref Dx12GpuResource? buffer,
        ref nint mappedAddress,
        ref int capacity)
    {
        if (buffer is null)
            return;

        buffer.Dispose();
        buffer = null;
        mappedAddress = 0;
        capacity = 0;
    }

    private static int RoundUpToPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value)
            result <<= 1;

        return result;
    }
}
