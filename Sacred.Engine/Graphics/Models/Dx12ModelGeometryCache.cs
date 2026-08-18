using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sacred.Granny;
using Sacred.Granny.Meshes;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Models;

/// <summary>Owns immutable index buffers and one CPU-writable vertex buffer per in-flight frame.</summary>
internal sealed class Dx12ModelGeometryCache : IDisposable
{
    private static readonly int VertexStride = Marshal.SizeOf<VertexPositionNormalTexture>();

    private readonly Dx12TextureUploader _uploader;
    private readonly int _frameCount;
    private readonly Dictionary<Mesh, ModelGpuMesh> _meshes = new(ReferenceEqualityComparer.Instance);

    public Dx12ModelGeometryCache(Dx12TextureUploader uploader, int frameCount)
    {
        _uploader = uploader;
        _frameCount = frameCount;
    }

    public ModelGpuMesh GetOrCreate(Mesh mesh, int frameIndex)
    {
        if (_meshes.TryGetValue(mesh, out var gpuMesh))
        {
            if (gpuMesh.VertexRevisions[frameIndex] != mesh.VertexRevision)
            {
                var updatedVertexBytes = MemoryMarshal.AsBytes(mesh.Vertices.AsSpan());
                Dx12TextureUploader.UpdateUploadBuffer(gpuMesh.VertexBuffers[frameIndex], updatedVertexBytes);
                gpuMesh.VertexRevisions[frameIndex] = mesh.VertexRevision;
            }

            return gpuMesh;
        }

        var vertexBytes = MemoryMarshal.AsBytes(mesh.Vertices.AsSpan());
        var indexBytes = MemoryMarshal.AsBytes(mesh.Indices.AsSpan());
        var vertexBuffers = new ID3D12Resource[_frameCount];
        var vertexBufferViews = new VertexBufferView[_frameCount];
        var vertexRevisions = new ulong[_frameCount];

        try
        {
            for (var index = 0; index < _frameCount; index++)
            {
                var vertexBuffer = _uploader.CreateUploadBuffer(vertexBytes);
                vertexBuffers[index] = vertexBuffer;
                vertexBufferViews[index] = new VertexBufferView(
                    vertexBuffer.GPUVirtualAddress,
                    (uint)vertexBytes.Length,
                    (uint)VertexStride);
                vertexRevisions[index] = mesh.VertexRevision;
            }

            var indexBuffer = _uploader.CreateUploadBuffer(indexBytes);
            gpuMesh = new ModelGpuMesh(
                vertexBuffers,
                vertexBufferViews,
                vertexRevisions,
                indexBuffer,
                new IndexBufferView(indexBuffer.GPUVirtualAddress, (uint)indexBytes.Length, Format.R16_UInt),
                mesh.Indices.Length);
        }
        catch
        {
            foreach (var vertexBuffer in vertexBuffers)
                vertexBuffer?.Dispose();
            throw;
        }

        _meshes.Add(mesh, gpuMesh);
        return gpuMesh;
    }

    public void Dispose()
    {
        foreach (var mesh in _meshes.Values)
            mesh.Dispose();
        _meshes.Clear();
    }
}

internal sealed class ModelGpuMesh(
    ID3D12Resource[] vertexBuffers,
    VertexBufferView[] vertexBufferViews,
    ulong[] vertexRevisions,
    ID3D12Resource indexBuffer,
    IndexBufferView indexBufferView,
    int indexCount) : IDisposable
{
    public ID3D12Resource[] VertexBuffers { get; } = vertexBuffers;
    public VertexBufferView[] VertexBufferViews { get; } = vertexBufferViews;
    public ulong[] VertexRevisions { get; } = vertexRevisions;
    public ID3D12Resource IndexBuffer { get; } = indexBuffer;
    public IndexBufferView IndexBufferView { get; } = indexBufferView;
    public int IndexCount { get; } = indexCount;

    public void Dispose()
    {
        foreach (var vertexBuffer in VertexBuffers)
            vertexBuffer.Dispose();
        IndexBuffer.Dispose();
    }
}
