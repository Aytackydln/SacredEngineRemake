using System;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.Uploads;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Skinning;

internal sealed class ModelGpuPoseSource : IDisposable
{
    public ModelGpuPoseSource(Dx12GeometryUploader copies, GpuPoseSourceData data)
    { Data = data; Buffer = copies.Upload(data.Bytes); }
    public GpuPoseSourceData Data { get; }
    public ID3D12Resource Buffer { get; }
    public void Retire(Dx12FrameContext frame) => frame.RetireResource(Buffer);
    public void Dispose() => Buffer.Dispose();
}
