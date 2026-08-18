using System;
using System.Collections.Generic;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Frames;

/// <summary>
/// Owns CPU-writable and allocator state that cannot be reused until one swap-chain frame retires.
/// </summary>
internal sealed class Dx12FrameContext : IDisposable
{
    private readonly List<ID3D12Resource> _retiredResources = new(64);
    private readonly List<int> _retiredSectorSrvSlots = new(8);
    private readonly List<int> _retiredModelSrvSlots = new(8);

    private ID3D12Resource? _spriteInstanceBuffer;
    private nint _spriteInstanceBufferMapped;
    private int _spriteInstanceCapacity;
    private ID3D12Resource? _lightHaloInstanceBuffer;
    private nint _lightHaloInstanceBufferMapped;
    private int _lightHaloInstanceCapacity;

    public Dx12FrameContext(int index, ID3D12CommandAllocator commandAllocator)
    {
        Index = index;
        CommandAllocator = commandAllocator;
    }

    public int Index { get; }
    public ID3D12CommandAllocator CommandAllocator { get; }
    public ulong FenceValue { get; set; }
    public List<ID3D12Resource> TransientResources => _retiredResources;
    public ID3D12Resource SpriteInstanceBuffer =>
        _spriteInstanceBuffer ?? throw new InvalidOperationException("The sprite instance buffer has not been created.");
    public nint SpriteInstanceBufferMapped => _spriteInstanceBufferMapped;
    public ID3D12Resource LightHaloInstanceBuffer =>
        _lightHaloInstanceBuffer ?? throw new InvalidOperationException("The light-halo instance buffer has not been created.");
    public nint LightHaloInstanceBufferMapped => _lightHaloInstanceBufferMapped;

    public void RetireResource(ID3D12Resource resource) => _retiredResources.Add(resource);

    public void RetireSectorSrvSlot(int slot) => _retiredSectorSrvSlots.Add(slot);

    public void RetireModelSrvSlot(int slot) => _retiredModelSrvSlots.Add(slot);

    public int ReleaseRetiredResources(Stack<int> freeSectorSrvSlots, Stack<int> freeModelSrvSlots)
    {
        foreach (var resource in _retiredResources)
            resource.Dispose();
        _retiredResources.Clear();

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
        ID3D12Device device,
        int instanceStride,
        int requiredCapacity)
    {
        EnsureInstanceCapacity(
            device,
            instanceStride,
            requiredCapacity,
            ref _spriteInstanceBuffer,
            ref _spriteInstanceBufferMapped,
            ref _spriteInstanceCapacity);
    }

    public unsafe void EnsureLightHaloInstanceCapacity(
        ID3D12Device device,
        int instanceStride,
        int requiredCapacity)
    {
        EnsureInstanceCapacity(
            device,
            instanceStride,
            requiredCapacity,
            ref _lightHaloInstanceBuffer,
            ref _lightHaloInstanceBufferMapped,
            ref _lightHaloInstanceCapacity);
    }

    private static unsafe void EnsureInstanceCapacity(
        ID3D12Device device,
        int instanceStride,
        int requiredCapacity,
        ref ID3D12Resource? buffer,
        ref nint mappedAddress,
        ref int capacity)
    {
        if (buffer is not null && capacity >= requiredCapacity)
            return;

        DisposeInstanceBuffer(ref buffer, ref mappedAddress, ref capacity);

        capacity = Math.Max(256, RoundUpToPowerOfTwo(requiredCapacity));
        var bufferBytes = checked((ulong)(capacity * instanceStride));
        var description = new ResourceDescription(
            ResourceDimension.Buffer,
            0,
            bufferBytes,
            1,
            1,
            1,
            Format.Unknown,
            1,
            0,
            TextureLayout.RowMajor,
            ResourceFlags.None);

        buffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload, 0, 0),
            HeapFlags.None,
            description,
            ResourceStates.GenericRead,
            null);

        void* mapped;
        buffer.Map(0, null, &mapped).CheckError();
        mappedAddress = (nint)mapped;
    }

    public void Dispose()
    {
        DisposeSpriteInstanceBuffer();
        DisposeInstanceBuffer(
            ref _lightHaloInstanceBuffer,
            ref _lightHaloInstanceBufferMapped,
            ref _lightHaloInstanceCapacity);

        foreach (var resource in _retiredResources)
            resource.Dispose();
        _retiredResources.Clear();

        CommandAllocator.Dispose();
    }

    private void DisposeSpriteInstanceBuffer()
    {
        DisposeInstanceBuffer(
            ref _spriteInstanceBuffer,
            ref _spriteInstanceBufferMapped,
            ref _spriteInstanceCapacity);
    }

    private static void DisposeInstanceBuffer(
        ref ID3D12Resource? buffer,
        ref nint mappedAddress,
        ref int capacity)
    {
        if (buffer is null)
            return;

        buffer.Unmap(0, null);
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
