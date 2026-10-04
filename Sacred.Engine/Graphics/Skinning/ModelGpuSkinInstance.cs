using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Engine.Graphics.Frames;
using Sacred.Granny.Animation;
using Sacred.Granny.Diagnostics;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Skinning;

/// <summary>Regular then rigid row-major matrices, one writable allocation per retired frame slot.</summary>
internal sealed class ModelGpuSkinInstance : IDisposable
{
    private readonly ID3D12Resource?[] _buffers;
    private readonly ulong[] _revisions;
    private readonly Matrix4x4[] _snapshot;
    public ModelGpuSkinInstance(GrnPose pose, int frameCount)
    { Pose = pose; _buffers = new ID3D12Resource[frameCount]; _revisions = new ulong[frameCount]; _snapshot = new Matrix4x4[Math.Max(1, pose.Data.Bones.Length) * 2]; }
    public GrnPose Pose { get; }
    public ModelGpuPoseInstance? GpuPose { get; set; }
    public long UploadedBytes { get; private set; }
    public int PaletteStrideBytes => GpuPose?.PaletteStride ?? _snapshot.Length / 2 * 64;
    public int PaletteOffsetBytes => GpuPose?.PaletteOffset ?? 0;
    public ulong Revision(int slot) => _revisions[slot];
    public ID3D12Resource? Buffer(int slot) => GpuPose is { } gpu ? gpu.Buffer(slot) : _buffers[slot];
    public bool Update(Dx12TextureUploader uploader, Dx12FrameContext retiredFrame)
    {
        var slot = retiredFrame.Index;
        if (GpuPose is not null || !Pose.IsEvaluated || _revisions[slot] == Pose.Revision) return false;
        using var measurement = AnimationPerformance.Measure(AnimationCpuStage.PaletteUpload);
        var revision = Pose.Revision;
        Pose.SkinTransforms.CopyTo(_snapshot);
        Pose.RigidSkinTransforms.CopyTo(_snapshot.AsSpan(_snapshot.Length / 2));
        if (Pose.Data.Bones.Length == 0) Array.Fill(_snapshot, Matrix4x4.Identity);
        var bytes = MemoryMarshal.AsBytes(_snapshot.AsSpan());
        if (_buffers[slot] is null) _buffers[slot] = uploader.CreateUploadBuffer(bytes);
        else Dx12TextureUploader.UpdateUploadBuffer(_buffers[slot]!, bytes);
        _revisions[slot] = revision; UploadedBytes += bytes.Length;
        AnimationPerformance.RecordPaletteUpload(bytes.Length);
        return true;
    }
    public void Retire(Dx12FrameContext frame)
    { for (var i = 0; i < _buffers.Length; i++) { if (_buffers[i] is { } buffer) frame.RetireResource(buffer); _buffers[i] = null; } }
    public void Dispose() { foreach (var buffer in _buffers) buffer?.Dispose(); Array.Clear(_buffers); }
}
