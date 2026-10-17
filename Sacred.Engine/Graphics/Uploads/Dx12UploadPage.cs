using System;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Uploads;

/// <summary>Persistently mapped staging memory owned by one fence-tracked copy context.</summary>
internal sealed class Dx12UploadPage : IDisposable
{
    public const int Capacity = 16 * 1024 * 1024;
    private readonly nint _mapped;
    private int _used;
    public ID3D12Resource Resource { get; }
    public int AvailableBytes => Capacity - AlignedOffset;
    private int AlignedOffset => (_used + 511) & ~511;

    public unsafe Dx12UploadPage(ID3D12Device device)
    {
        Resource = device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
            ResourceDescription.Buffer(Capacity), ResourceStates.GenericRead);
        try
        {
            void* mapped;
            Resource.Map(0, null, &mapped).CheckError();
            _mapped = (nint)mapped;
        }
        catch { Resource.Dispose(); throw; }
    }

    public nint Allocate(int size, out ulong offset)
    {
        if (size <= 0 || size > AvailableBytes) throw new ArgumentOutOfRangeException(nameof(size));
        var start = AlignedOffset;
        _used = start + size;
        offset = (ulong)start;
        return _mapped + start;
    }

    /// <summary>Called only after the context's copy fence retires.</summary>
    public void Reset() => _used = 0;
    public void Dispose() { Resource.Unmap(0); Resource.Dispose(); }
}
