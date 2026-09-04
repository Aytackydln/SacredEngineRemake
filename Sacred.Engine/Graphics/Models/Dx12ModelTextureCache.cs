using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Scene;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Models;

/// <summary>Reconciles scene materials only when the model set changes and incrementally uploads them.</summary>
internal sealed class Dx12ModelTextureCache : IDisposable
{
    private const int MaxConcurrentLoads = 2;
    private const int UploadBatchSize = 1;

    private readonly AssetManager _assets;
    private readonly Dx12TextureUploader _uploader;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly CpuDescriptorHandle _srvHeapStart;
    private readonly int _descriptorSize;
    private readonly Stack<int> _freeSrvSlots;
    private readonly Dictionary<string, ModelTexture> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<ModelTexture> _pendingLoads = new();
    private readonly ConcurrentQueue<CompletedTextureLoad> _completedLoads = new();
    private readonly List<Task> _loadTasks = [];
    private readonly HashSet<string> _activeNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _namesToRemove;

    private ulong _preparedModelSetRevision = ulong.MaxValue;

    public Dx12ModelTextureCache(
        AssetManager assets,
        Dx12TextureUploader uploader,
        ID3D12GraphicsCommandList commandList,
        ID3D12DescriptorHeap srvHeap,
        int descriptorSize,
        Stack<int> freeSrvSlots,
        int maximumTextureCount)
    {
        _assets = assets;
        _uploader = uploader;
        _commandList = commandList;
        _srvHeapStart = srvHeap.GetCPUDescriptorHandleForHeapStart();
        _descriptorSize = descriptorSize;
        _freeSrvSlots = freeSrvSlots;
        _namesToRemove = new List<string>(maximumTextureCount);
    }

    public ModelTextureStats Stats
    {
        get
        {
            var stats = new ModelTextureStats();
            foreach (var texture in _textures.Values)
            {
                if (texture.Resource is not null)
                {
                    stats.Ready++;
                    continue;
                }

                switch (texture.Stage)
                {
                    case ModelTextureStage.Queued:
                    case ModelTextureStage.LoadingAsset:
                        stats.Loading++;
                        break;
                    case ModelTextureStage.ReadyForGpu:
                        stats.Uploading++;
                        break;
                    case ModelTextureStage.Failed:
                        stats.Failed++;
                        break;
                }
            }

            return stats;
        }
    }

    public void PrepareFrame(SceneState scene, Dx12FrameContext frame)
    {
        if (_preparedModelSetRevision != scene.ModelSetRevision)
        {
            ReconcileScene(scene.Models, frame);
            _preparedModelSetRevision = scene.ModelSetRevision;
        }

        CollectCompletedLoads(frame);
        StartPendingLoads();
    }

    public ModelTexture? Get(string? textureName)
    {
        if (string.IsNullOrWhiteSpace(textureName))
            return null;

        return _textures.TryGetValue(textureName, out var texture) && texture.Resource is not null
            ? texture
            : null;
    }

    public void WaitForPendingLoads() => Task.WhenAll(_loadTasks).GetAwaiter().GetResult();

    public void Dispose()
    {
        WaitForPendingLoads();

        foreach (var texture in _textures.Values)
            texture.Resource?.Dispose();
        _textures.Clear();

        while (_completedLoads.TryDequeue(out var completed))
        {
            if (completed.SrvSlot >= 0)
                _freeSrvSlots.Push(completed.SrvSlot);
        }
    }

    private void ReconcileScene(IReadOnlyList<SceneModel> models, Dx12FrameContext frame)
    {
        _activeNames.Clear();
        foreach (var model in models)
        foreach (var surface in model.Mesh.Surfaces)
        {
            var reference = model.ResolveTextureReference(surface.TextureName);
            if (!string.IsNullOrWhiteSpace(reference.TextureName))
                _activeNames.Add(reference.TextureName);
            if (!string.IsNullOrWhiteSpace(reference.OverlayTextureName))
                _activeNames.Add(reference.OverlayTextureName);
        }
        foreach (var model in models)
        {
            if (model.EquipmentEffects is null)
                continue;
            foreach (var textureName in model.EquipmentEffects.TextureNames)
                _activeNames.Add(textureName);
        }

        _namesToRemove.Clear();
        foreach (var pair in _textures)
        {
            if (_activeNames.Contains(pair.Key))
                continue;

            _namesToRemove.Add(pair.Key);
            var texture = pair.Value;
            if (texture.Resource is not null)
            {
                frame.RetireResource(texture.Resource);
                texture.Resource = null;
            }

            if (texture.SrvSlot >= 0)
            {
                frame.RetireModelSrvSlot(texture.SrvSlot);
                texture.SrvSlot = -1;
            }
        }

        foreach (var textureName in _namesToRemove)
            _textures.Remove(textureName);

        // Queue base textures before optional overlays so characters become complete progressively.
        foreach (var model in models)
        foreach (var surface in model.Mesh.Surfaces)
            Request(model.ResolveTextureReference(surface.TextureName).TextureName);

        foreach (var model in models)
        foreach (var surface in model.Mesh.Surfaces)
        {
            var reference = model.ResolveTextureReference(surface.TextureName);
            if (reference.HasOverlay)
                Request(reference.OverlayTextureName);
        }

        foreach (var model in models)
        {
            if (model.EquipmentEffects is null)
                continue;
            foreach (var textureName in model.EquipmentEffects.TextureNames)
                Request(textureName);
        }
    }

    private void Request(string? textureName)
    {
        if (string.IsNullOrWhiteSpace(textureName))
            return;

        if (!_textures.TryGetValue(textureName, out var texture))
        {
            texture = new ModelTexture(textureName);
            _textures.Add(textureName, texture);
        }

        if (texture.Resource is not null ||
            texture.Pending ||
            texture.Failed ||
            texture.Stage != ModelTextureStage.None)
        {
            return;
        }

        texture.Stage = ModelTextureStage.Queued;
        _pendingLoads.Enqueue(texture);
    }

    private void StartPendingLoads()
    {
        _loadTasks.RemoveAll(static task => task.IsCompleted);
        while (_loadTasks.Count < MaxConcurrentLoads &&
               _freeSrvSlots.Count > 0 &&
               _pendingLoads.TryDequeue(out var texture))
        {
            if (!_textures.TryGetValue(texture.Name, out var activeTexture) ||
                !ReferenceEquals(activeTexture, texture) ||
                texture.Resource is not null ||
                texture.Pending ||
                texture.Failed ||
                texture.Stage != ModelTextureStage.Queued)
            {
                continue;
            }

            texture.Pending = true;
            texture.Stage = ModelTextureStage.LoadingAsset;
            var slot = _freeSrvSlots.Pop();
            _loadTasks.Add(LoadAsync(texture, slot));
        }
    }

    private async Task LoadAsync(ModelTexture texture, int srvSlot)
    {
        try
        {
            var asset = await _assets.LoadModelTextureAsync(texture.Name).ConfigureAwait(false);
            var hasTranslucentPixels = HasTranslucentPixels(asset.Rgba8);
            texture.Stage = ModelTextureStage.ReadyForGpu;
            _completedLoads.Enqueue(new CompletedTextureLoad(
                texture,
                asset,
                srvSlot,
                hasTranslucentPixels,
                null));
        }
        catch (Exception exception)
        {
            texture.Stage = ModelTextureStage.Failed;
            _completedLoads.Enqueue(new CompletedTextureLoad(texture, null, srvSlot, false, exception));
        }
    }

    private void CollectCompletedLoads(Dx12FrameContext frame)
    {
        var uploaded = 0;
        while (uploaded < UploadBatchSize && _completedLoads.TryDequeue(out var completed))
        {
            var requestedTexture = completed.Texture;
            if (!_textures.TryGetValue(requestedTexture.Name, out var texture) ||
                !ReferenceEquals(texture, requestedTexture))
            {
                if (completed.SrvSlot >= 0)
                    _freeSrvSlots.Push(completed.SrvSlot);
                continue;
            }

            texture.Pending = false;
            if (completed.Error is not null || completed.Asset is null)
            {
                texture.Failed = true;
                texture.Stage = ModelTextureStage.Failed;
                if (completed.SrvSlot >= 0)
                    _freeSrvSlots.Push(completed.SrvSlot);
                continue;
            }

            uploaded++;
            ID3D12Resource? resource = null;
            try
            {
                var asset = completed.Asset;
                resource = _uploader.UploadRgbaTexture(
                    _commandList,
                    asset.Width,
                    asset.Height,
                    asset.Rgba8,
                    frame.TransientResources);
                _uploader.CreateShaderResourceView(resource, SrvCpuHandle(completed.SrvSlot));
                texture.SrvSlot = completed.SrvSlot;
                texture.Resource = resource;
                texture.HasTranslucentPixels = completed.HasTranslucentPixels;
                _assets.ReleaseModelTexture(texture.Name, asset);
            }
            catch
            {
                resource?.Dispose();
                _freeSrvSlots.Push(completed.SrvSlot);
                texture.Failed = true;
                texture.Stage = ModelTextureStage.Failed;
            }
        }
    }

    private CpuDescriptorHandle SrvCpuHandle(int index) => _srvHeapStart + index * _descriptorSize;

    private static bool HasTranslucentPixels(ReadOnlySpan<byte> rgba8)
    {
        for (var index = 3; index < rgba8.Length; index += 4)
        {
            var alpha = rgba8[index];
            if (alpha is not 0 and not byte.MaxValue)
                return true;
        }

        return false;
    }

    private readonly record struct CompletedTextureLoad(
        ModelTexture Texture,
        TextureAsset? Asset,
        int SrvSlot,
        bool HasTranslucentPixels,
        Exception? Error);

    internal enum ModelTextureStage
    {
        None,
        Queued,
        LoadingAsset,
        ReadyForGpu,
        Failed
    }

    internal sealed class ModelTexture(string name)
    {
        private int _stage;

        public string Name { get; } = name;
        public ID3D12Resource? Resource { get; set; }
        public int SrvSlot { get; set; } = -1;
        public bool Pending { get; set; }
        public bool Failed { get; set; }
        public bool HasTranslucentPixels { get; set; }
        public ModelTextureStage Stage
        {
            get => (ModelTextureStage)Volatile.Read(ref _stage);
            set => Volatile.Write(ref _stage, (int)value);
        }
    }
}

internal struct ModelTextureStats
{
    public int Ready;
    public int Loading;
    public int Uploading;
    public int Failed;
}
