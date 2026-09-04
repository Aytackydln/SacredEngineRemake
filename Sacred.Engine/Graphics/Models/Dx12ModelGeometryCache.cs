using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Sacred.Engine.Assets;
using Sacred.Engine.Scene;
using Sacred.Granny.Meshes;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Models;

/// <summary>Owns immutable index buffers and one CPU-writable vertex buffer per in-flight frame.</summary>
internal sealed class Dx12ModelGeometryCache : IDisposable
{
    private static readonly int VertexStride = Marshal.SizeOf<VertexPositionNormalTexture>();

    private readonly Dx12TextureUploader _uploader;
    private readonly AssetManager _assets;
    private readonly int _frameCount;
    private readonly Dictionary<Mesh, ModelGpuMesh> _meshes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Mesh, Task<ModelGpuMesh>> _loads = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Mesh> _failedMeshes = new(ReferenceEqualityComparer.Instance);
    private readonly List<Mesh> _completedLoads = [];

    public Dx12ModelGeometryCache(AssetManager assets, Dx12TextureUploader uploader, int frameCount)
    {
        _assets = assets;
        _uploader = uploader;
        _frameCount = frameCount;
    }

    public bool Prepare(IReadOnlyList<SceneModel> models)
    {
        CollectCompletedLoads();
        var ready = true;
        foreach (var model in models)
        {
            ready &= Request(model.Mesh);
            if (model.EquipmentEffects is { } effects)
                ready &= Request(effects.Mesh);
        }

        return ready;
    }

    public bool TryGetOrRequest(Mesh mesh, int frameIndex, out ModelGpuMesh gpuMesh)
    {
        CollectCompletedLoads();
        if (_meshes.TryGetValue(mesh, out gpuMesh!))
        {
            if (gpuMesh.VertexRevisions[frameIndex] != mesh.VertexRevision)
            {
                var updatedVertexBytes = MemoryMarshal.AsBytes(mesh.Vertices.AsSpan());
                Dx12TextureUploader.UpdateUploadBuffer(gpuMesh.VertexBuffers[frameIndex], updatedVertexBytes);
                gpuMesh.VertexRevisions[frameIndex] = mesh.VertexRevision;
            }

            return true;
        }

        Request(mesh);
        gpuMesh = null!;
        return false;
    }

    public void WaitForPendingLoads()
    {
        foreach (var load in _loads.Values)
        {
            try
            {
                load.GetAwaiter().GetResult().Dispose();
            }
            catch
            {
                // A failed preparation never published a GPU resource.
            }
        }
        _loads.Clear();
    }

    public void Dispose()
    {
        WaitForPendingLoads();
        foreach (var mesh in _meshes.Values)
            mesh.Dispose();
        _meshes.Clear();
    }

    private bool Request(Mesh mesh)
    {
        if (mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 ||
            _meshes.ContainsKey(mesh) || _failedMeshes.Contains(mesh))
        {
            return true;
        }

        if (!_loads.ContainsKey(mesh))
            _loads.Add(mesh, _assets.ScheduleVisiblePreparation(() => CreateGpuMesh(mesh)));
        return false;
    }

    private void CollectCompletedLoads()
    {
        _completedLoads.Clear();
        foreach (var pair in _loads)
            if (pair.Value.IsCompleted)
                _completedLoads.Add(pair.Key);

        foreach (var mesh in _completedLoads)
        {
            var load = _loads[mesh];
            _loads.Remove(mesh);
            if (load.IsCompletedSuccessfully)
            {
                _meshes.Add(mesh, load.Result);
                continue;
            }

            _failedMeshes.Add(mesh);
            EngineLog.WriteLine($"Model GPU geometry preparation failed: {load.Exception}");
        }
    }

    private ModelGpuMesh CreateGpuMesh(Mesh mesh)
    {
        var vertexBytes = MemoryMarshal.AsBytes(mesh.Vertices.AsSpan());
        var indexBytes = MemoryMarshal.AsBytes(mesh.Indices.AsSpan());
        var vertexBuffers = new ID3D12Resource[_frameCount];
        var vertexBufferViews = new VertexBufferView[_frameCount];
        var vertexRevisions = new ulong[_frameCount];
        ModelGpuMesh gpuMesh;

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

        return gpuMesh;
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
