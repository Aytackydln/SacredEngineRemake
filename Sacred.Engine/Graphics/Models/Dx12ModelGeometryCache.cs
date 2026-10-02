using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics.Uploads;
using Sacred.Engine.Scene;
using Sacred.Granny.Meshes;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Models;

/// <summary>Owns DEFAULT geometry and per-frame upload vertices for CPU-deformed meshes.</summary>
internal sealed class Dx12ModelGeometryCache : IDisposable
{
    private static readonly int VertexStride = Marshal.SizeOf<VertexPositionNormalTexture>();

    private readonly Dx12TextureUploader _uploader;
    private readonly Dx12GeometryUploader _geometryUploader;
    private readonly AssetManager _assets;
    private readonly int _frameCount;
    private readonly Dictionary<Mesh, ModelGpuMesh> _meshes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Mesh, Task<ModelGpuMesh>> _loads = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Mesh> _failedMeshes = new(ReferenceEqualityComparer.Instance);
    private readonly List<Mesh> _completedLoads = [];

    public Dx12ModelGeometryCache(AssetManager assets, ID3D12Device device, Dx12TextureUploader uploader, int frameCount)
    {
        _assets = assets;
        _uploader = uploader;
        _geometryUploader = new Dx12GeometryUploader(device, uploader);
        _frameCount = frameCount;
    }

    public bool Prepare(IReadOnlyList<SceneModel> models)
    {
        CollectCompletedLoads();
        var ready = true;
        foreach (var model in models)
        {
            if (model.Geometry.Kind != SceneModelGeometryKind.GpuSkinned) ready &= Request(model.Mesh);
            if (model.EquipmentEffects is { } effects)
                ready &= Request(effects.Mesh);
        }

        return ready;
    }

    public bool TryGetOrRequest(Mesh mesh, int frameIndex, out ModelGpuMesh gpuMesh)
    {
        // Prepare publishes completed loads once before recording models and shadows.
        // Scanning every pending load per draw makes preparation quadratic in scene size.
        if (_meshes.TryGetValue(mesh, out gpuMesh!))
        {
            gpuMesh.UpdateVertices(_uploader, mesh, frameIndex);

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
        _geometryUploader.Dispose();
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
        var readyCount = 0;
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
                readyCount++;
                continue;
            }

            _failedMeshes.Add(mesh);
            EngineLog.WriteLine($"Model GPU geometry preparation failed: {load.Exception}");
        }

        if (readyCount > 0)
            EngineLog.WriteLine($"Model GPU geometry copies completed: {readyCount}; {_meshes.Count} meshes resident.");
    }

    private ModelGpuMesh CreateGpuMesh(Mesh mesh)
    {
        // Capture the revision before taking a snapshot so concurrent animation cannot mark
        // an older snapshot as current. The render thread refreshes it on the next draw.
        var revision = mesh.VertexRevision;
        var dynamicVertices = mesh.HasDynamicVertices;
        var vertices = (VertexPositionNormalTexture[])mesh.Vertices.Clone();
        var vertexBytes = MemoryMarshal.AsBytes(vertices.AsSpan());
        var indexBytes = MemoryMarshal.AsBytes(mesh.Indices.AsSpan());
        var vertexBuffers = new ID3D12Resource[_frameCount];
        var vertexBufferViews = new VertexBufferView[_frameCount];
        var vertexRevisions = new ulong[_frameCount];
        ID3D12Resource? staticVertices = null;
        ID3D12Resource? indexBuffer = null;

        try
        {
            if (!dynamicVertices)
                staticVertices = _geometryUploader.Upload(vertexBytes);
            for (var index = 0; index < _frameCount; index++)
            {
                var vertexBuffer = staticVertices ?? _uploader.CreateUploadBuffer(vertexBytes);
                vertexBuffers[index] = vertexBuffer;
                vertexBufferViews[index] = new VertexBufferView(
                    vertexBuffer.GPUVirtualAddress,
                    (uint)vertexBytes.Length,
                    (uint)VertexStride);
                vertexRevisions[index] = revision;
            }

            indexBuffer = _geometryUploader.Upload(indexBytes);
            return new ModelGpuMesh(
                vertexBuffers,
                vertexBufferViews,
                vertexRevisions,
                dynamicVertices,
                indexBuffer,
                new IndexBufferView(indexBuffer.GPUVirtualAddress, (uint)indexBytes.Length, Format.R16_UInt),
                mesh.Indices.Length);
        }
        catch
        {
            if (staticVertices is not null)
                staticVertices.Dispose();
            else
                foreach (var vertexBuffer in vertexBuffers)
                    vertexBuffer?.Dispose();
            indexBuffer?.Dispose();
            throw;
        }
    }
}
