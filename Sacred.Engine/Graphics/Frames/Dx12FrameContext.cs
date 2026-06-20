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
        if (_spriteInstanceBuffer is not null && _spriteInstanceCapacity >= requiredCapacity)
            return;

        DisposeSpriteInstanceBuffer();

        _spriteInstanceCapacity = Math.Max(256, RoundUpToPowerOfTwo(requiredCapacity));
        var bufferBytes = checked((ulong)(_spriteInstanceCapacity * instanceStride));
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

        _spriteInstanceBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload, 0, 0),
            HeapFlags.None,
            description,
            ResourceStates.GenericRead,
            null);

        void* mapped;
        _spriteInstanceBuffer.Map(0, null, &mapped).CheckError();
        _spriteInstanceBufferMapped = (nint)mapped;
    }

    public void Dispose()
    {
        DisposeSpriteInstanceBuffer();

        foreach (var resource in _retiredResources)
            resource.Dispose();
        _retiredResources.Clear();

        CommandAllocator.Dispose();
    }

    private void DisposeSpriteInstanceBuffer()
    {
        if (_spriteInstanceBuffer is null)
            return;

        _spriteInstanceBuffer.Unmap(0, null);
        _spriteInstanceBuffer.Dispose();
        _spriteInstanceBuffer = null;
        _spriteInstanceBufferMapped = 0;
        _spriteInstanceCapacity = 0;
    }

    private static int RoundUpToPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value)
            result <<= 1;

        return result;
    }
}
