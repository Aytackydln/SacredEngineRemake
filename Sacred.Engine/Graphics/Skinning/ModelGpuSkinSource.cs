using System;
using System.Collections.Generic;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.Uploads;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Skinning;

/// <summary>Shared immutable DEFAULT resources, published only after all copy fences complete.</summary>
internal sealed class ModelGpuSkinSource : IDisposable
{
    private readonly List<ID3D12Resource> _owned = [];
    public ModelGpuSkinSource(Dx12GeometryUploader uploader, GpuSkinSourceData data)
    {
        try
        {
            Vertices = Upload(data.Vertices); Influences = Upload(data.Influences);
            Projection = Upload(data.Projection); Indices = Upload(data.Indices);
            IndexView = new(Indices.GPUVirtualAddress, (uint)data.Indices.Length, Format.R16_UInt);
            IndexCount = data.IndexCount;
            ByteCount = (long)data.Vertices.Length + data.Influences.Length + data.Projection.Length + data.Indices.Length;
        }
        catch { Dispose(); throw; }
        ID3D12Resource Upload(byte[] bytes) { var resource = uploader.Upload(bytes); _owned.Add(resource); return resource; }
    }
    public ID3D12Resource Vertices { get; }
    public ID3D12Resource Influences { get; }
    public ID3D12Resource Projection { get; }
    public ID3D12Resource Indices { get; }
    public IndexBufferView IndexView { get; }
    public int IndexCount { get; }
    public long ByteCount { get; }
    public void Retire(Dx12FrameContext frame) { foreach (var r in _owned) frame.RetireResource(r); _owned.Clear(); }
    public void Dispose() { foreach (var r in _owned) r.Dispose(); _owned.Clear(); }
}
