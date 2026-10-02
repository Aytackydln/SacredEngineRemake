using System;
using System.Runtime.InteropServices;
using Sacred.Engine.Graphics.Frames;
using Sacred.Particles;
using Sacred.Shaders;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Particles;

[StructLayout(LayoutKind.Sequential)]
internal struct ParticleGpuDraw
{
    public StaticSpriteInstance Sprite;
    public float SortDepth;
    public int TileX, TileY, DrawOrder;
    public uint Sequence, Encoding, Valid;
}
[StructLayout(LayoutKind.Sequential)]
internal readonly struct ParticleGpuSpawn(int slot, ParticleSimulationState state)
{
    public readonly int Slot = slot;
    public readonly ParticleSimulationState State = state;
}

internal sealed class ParticleGpuBuffers : IDisposable
{
    public ParticleGpuBuffers(ID3D12Device device, Dx12TextureUploader uploader, ParticleGpuBatch batch)
    {
        States = Create(device, checked(batch.Capacity * Marshal.SizeOf<ParticleSimulationState>()));
        try
        {
            var colors = new uint[256];
            for (var i = 0; i < colors.Length; i++) colors[i] = batch.Parameters.Colors.Count > i
                ? batch.Parameters.Colors[i] : batch.Parameters.Colors.Count > 0
                    ? batch.Parameters.Colors[0] : batch.Parameters.Emission.Color;
            Colors = uploader.CreateUploadBuffer(MemoryMarshal.AsBytes(colors.AsSpan()));
            CommittedBytes = device.GetResourceAllocationInfo(0,States.Description).SizeInBytes +
                device.GetResourceAllocationInfo(0,Colors.Description).SizeInBytes;
        }
        catch { States.Dispose(); throw; }
    }
    public ID3D12Resource States { get; }
    public ID3D12Resource Colors { get; }
    public bool Initialized { get; set; }
    public ulong CommittedBytes { get; }
    public long Bytes => checked((long)(States.Description.Width + Colors.Description.Width));
    public static ID3D12Resource Create(ID3D12Device device, int bytes, HeapType heap = HeapType.Default) =>
        device.CreateCommittedResource(new HeapProperties(heap), HeapFlags.None,
            new ResourceDescription(ResourceDimension.Buffer,0,(ulong)Math.Max(4,bytes),1,1,1,Format.Unknown,
                1,0,TextureLayout.RowMajor,heap == HeapType.Default ? ResourceFlags.AllowUnorderedAccess : ResourceFlags.None),
            heap == HeapType.Readback ? ResourceStates.CopyDest : ResourceStates.UnorderedAccess,null);
    public void Retire(Dx12FrameContext frame) { frame.RetireResource(States); frame.RetireResource(Colors); }
    public void Dispose() { States.Dispose(); Colors.Dispose(); }
}
