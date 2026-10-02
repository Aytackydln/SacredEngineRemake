using System;
using System.Runtime.InteropServices;
using Sacred.Engine.Graphics.Frames;
using Sacred.Granny.Diagnostics;
using Sacred.Granny.Meshes;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Models;

/// <summary>Shares static vertices across frames, or owns one writable buffer per in-flight frame.</summary>
internal sealed class ModelGpuMesh(
    ID3D12Resource[] vertexBuffers,
    VertexBufferView[] vertexBufferViews,
    ulong[] vertexRevisions,
    bool dynamicVertices,
    ID3D12Resource indexBuffer,
    IndexBufferView indexBufferView,
    int indexCount) : IDisposable
{
    private ID3D12Resource? _retiredStaticVertices;
    private bool _dynamicVertices = dynamicVertices;

    public ID3D12Resource[] VertexBuffers { get; private set; } = vertexBuffers;
    public VertexBufferView[] VertexBufferViews { get; private set; } = vertexBufferViews;
    public ulong[] VertexRevisions { get; } = vertexRevisions;
    public ID3D12Resource IndexBuffer { get; } = indexBuffer;
    public IndexBufferView IndexBufferView { get; } = indexBufferView;
    public int IndexCount { get; } = indexCount;

    public void UpdateVertices(Dx12TextureUploader uploader, Mesh mesh, int frameIndex)
    {
        if (VertexRevisions[frameIndex] == mesh.VertexRevision)
            return;

        using var measurement = AnimationPerformance.Measure(AnimationCpuStage.VertexUpload);

        var bytes = MemoryMarshal.AsBytes(mesh.Vertices.AsSpan());
        if (!_dynamicVertices)
        {
            // A previously static mesh was modified. Never map DEFAULT memory or release
            // the shared buffer while an earlier frame might still be reading it.
            var buffers = new ID3D12Resource[VertexBuffers.Length];
            var views = new VertexBufferView[buffers.Length];
            try
            {
                for (var index = 0; index < buffers.Length; index++)
                {
                    buffers[index] = uploader.CreateUploadBuffer(bytes);
                    views[index] = new VertexBufferView(buffers[index].GPUVirtualAddress,
                        (uint)bytes.Length, VertexBufferViews[index].StrideInBytes);
                }
            }
            catch
            {
                foreach (var buffer in buffers) buffer?.Dispose();
                throw;
            }

            _retiredStaticVertices = VertexBuffers[0];
            VertexBuffers = buffers;
            VertexBufferViews = views;
            Array.Fill(VertexRevisions, mesh.VertexRevision);
            _dynamicVertices = true;
            AnimationPerformance.RecordVertexUpload(checked(bytes.Length * buffers.Length));
            return;
        }

        Dx12TextureUploader.UpdateUploadBuffer(VertexBuffers[frameIndex], bytes);
        VertexRevisions[frameIndex] = mesh.VertexRevision;
        AnimationPerformance.RecordVertexUpload(bytes.Length);
    }

    public void Retire(Dx12FrameContext frame)
    {
        if (_dynamicVertices)
            foreach (var buffer in VertexBuffers) frame.RetireResource(buffer);
        else frame.RetireResource(VertexBuffers[0]);
        if (_retiredStaticVertices is not null) frame.RetireResource(_retiredStaticVertices);
        frame.RetireResource(IndexBuffer);
    }

    public void Dispose()
    {
        // Static frames reference the same resource; release each allocation once.
        if (_dynamicVertices)
            foreach (var buffer in VertexBuffers) buffer.Dispose();
        else
            VertexBuffers[0].Dispose();
        _retiredStaticVertices?.Dispose();
        IndexBuffer.Dispose();
    }
}
