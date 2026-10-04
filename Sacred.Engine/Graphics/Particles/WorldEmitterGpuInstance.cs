using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sacred.Engine.Graphics.Frames;
using Sacred.World.Particles;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Particles;

internal sealed class WorldEmitterGpuInstance : IDisposable
{
    public WorldEmitterGpuInstance(ID3D12Device device, Dx12TextureUploader uploader, WorldGpuEmitter emitter)
    {
        SourceData = new(emitter);
        Source = uploader.CreateUploadBuffer(SourceData.Bytes);
        try
        {
            States = ParticleGpuBuffers.Create(device, SourceData.StateBytes);
            Colors = new ID3D12Resource[emitter.Parameters.Length];
            for (var s = 0; s < Colors.Length; s++)
            {
                var p = emitter.Parameters[s]; var colors = new uint[256];
                for (var i = 0; i < colors.Length; i++) colors[i] = p.Colors.Count > i ? p.Colors[i] :
                    p.Colors.Count > 0 ? p.Colors[0] : p.Emission.Color;
                Colors[s] = uploader.CreateUploadBuffer(MemoryMarshal.AsBytes(colors.AsSpan()));
            }
            if (emitter.Definition.Halo is { } halo)
            {
                var colors = new uint[256]; Array.Fill(colors, halo.Color);
                HaloColors = uploader.CreateUploadBuffer(MemoryMarshal.AsBytes(colors.AsSpan()));
            }
            foreach (var resource in Resources())
                CommittedBytes += device.GetResourceAllocationInfo(0, resource.Description).SizeInBytes;
        }
        catch { Dispose(); throw; }
    }
    public WorldEmitterGpuSource SourceData { get; }
    public ID3D12Resource Source { get; }
    public ID3D12Resource States { get; } = null!;
    public ID3D12Resource[] Colors { get; } = [];
    public ID3D12Resource? HaloColors { get; }
    public bool Initialized { get; set; }
    public ulong AppliedSequence { get; set; }
    public bool CommittedInitialized { get; private set; }
    public ulong CommittedSequence { get; private set; }
    public void BeginRecording() { Initialized = CommittedInitialized; AppliedSequence = CommittedSequence; }
    public void Commit()
    {
        CommittedInitialized = Initialized; CommittedSequence = AppliedSequence;
        SourceData.Emitter.Acknowledge(CommittedSequence);
    }
    public ulong CommittedBytes { get; }
    public ulong Bytes { get { ulong bytes = 0; foreach (var r in Resources()) bytes += r.Description.Width; return bytes; } }
    public ResourceStates State { get; set; } = ResourceStates.UnorderedAccess;
    private IEnumerable<ID3D12Resource> Resources()
    {
        yield return Source; if (States is not null) yield return States;
        foreach (var colors in Colors) if (colors is not null) yield return colors;
        if (HaloColors is not null) yield return HaloColors;
    }
    public void Retire(Dx12FrameContext frame) { foreach (var r in Resources()) frame.RetireResource(r); }
    public void Dispose() { foreach (var r in Resources()) r.Dispose(); }
}
