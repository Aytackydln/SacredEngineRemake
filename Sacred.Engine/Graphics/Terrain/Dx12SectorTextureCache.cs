using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Sacred.Core.World.Sector;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Rendering;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Terrain;

/// <summary>Owns the bounded, fence-safe GPU cache and dedicated sector-composition queue.</summary>
internal sealed class Dx12SectorTextureCache : IDisposable
{
    private const int TexturesPerSector = 5;

    private readonly int _maximumTextureCount;
    private readonly Dx12TextureUploader _uploader;
    private readonly Dx12SectorCompositionWorker _compositionWorker;
    private readonly CpuDescriptorHandle _srvHeapStart;
    private readonly int _descriptorSize;
    private readonly Dictionary<SectorCoord, SectorTexture> _textures = new();
    private readonly HashSet<SectorCoord> _pendingUploads = [];
    private readonly List<SectorCoord> _texturesToRetire = new(9);
    private readonly Stack<int> _freeSrvSlots;

    private Dictionary<SectorCoord, TerrainSectorComposition> _wantedCompositions = new();
    private int _retiringSrvSlotCount;
    private long _requestSequence;
    private volatile bool _stopped;

    public Dx12SectorTextureCache(
        ID3D12Device device,
        Dx12TextureUploader uploader,
        ID3D12DescriptorHeap srvHeap,
        int descriptorSize,
        int maximumTextureCount)
    {
        _maximumTextureCount = maximumTextureCount;
        _uploader = uploader;
        _compositionWorker = new Dx12SectorCompositionWorker(
            device,
            uploader,
            maximumTextureCount,
            IsWanted);
        _srvHeapStart = srvHeap.GetCPUDescriptorHandleForHeapStart();
        _descriptorSize = descriptorSize;
        _freeSrvSlots = new Stack<int>(maximumTextureCount * TexturesPerSector);
        for (var index = maximumTextureCount * TexturesPerSector - 1; index >= 0; index--)
            _freeSrvSlots.Push(index);

    }

    public int Count => _textures.Count;
    public int PendingUploadCount => _pendingUploads.Count;
    public int MaximumTextureCount => _maximumTextureCount;
    public Stack<int> FreeSrvSlots => _freeSrvSlots;

    /// <summary>
    /// Gives the compositor one opportunity after foreground commands have been submitted.
    /// This prevents background GPU work from racing ahead while frames are being recorded.
    /// </summary>
    public void OnForegroundFrameSubmitted() => _compositionWorker.OnForegroundFrameSubmitted();

    public void OnFrameRetired(int releasedSectorSlotCount)
    {
        if (releasedSectorSlotCount == 0)
            return;

        _retiringSrvSlotCount -= releasedSectorSlotCount;
        EngineLog.WriteLine(
            $"Sector GPU textures released: {releasedSectorSlotCount / TexturesPerSector}; replacement loading may resume.");
    }

    public void PrepareFrame(
        IReadOnlyList<TerrainSectorComposition> images,
        Vector2 cameraWorldCenter,
        Vector2 cameraMovementDirection,
        Dx12FrameContext frame)
    {
        _compositionWorker.UpdateSchedule(cameraWorldCenter, cameraMovementDirection);
        UpdateWantedCompositions(images);
        RetireUnneededTextures(frame);
        CollectCompletedUploads(frame);

        // Retired textures remain alive until the GPU fence for their last frame completes.
        // Do not allocate replacement render targets during that interval: a later frame will
        // observe the returned descriptor slots and queue the new work without blocking here.
        if (_retiringSrvSlotCount == 0)
            QueueMissingUploads(images);
    }

    public bool TryGet(SectorCoord coord, out SectorTextureView texture)
    {
        if (_textures.TryGetValue(coord, out var cached))
        {
            texture = new SectorTextureView(
                cached.BaseSrvSlot,
                cached.LiquidCoverSrvSlot,
                cached.StairsDebugSrvSlot,
                cached.BlockedAreaDebugSrvSlot,
                cached.TerrainTopologyDebugSrvSlot);
            return true;
        }

        texture = default;
        return false;
    }

    public void StopWorker()
    {
        if (_stopped)
            return;

        _stopped = true;
        Volatile.Write(ref _wantedCompositions, new Dictionary<SectorCoord, TerrainSectorComposition>());
        _compositionWorker.Stop();

        while (_compositionWorker.TryDequeueCompleted(out var composition))
        {
            composition.Composed?.Dispose();
            _pendingUploads.Remove(composition.Coord);
            _freeSrvSlots.Push(composition.BaseSrvSlot);
            _freeSrvSlots.Push(composition.LiquidCoverSrvSlot);
            _freeSrvSlots.Push(composition.StairsDebugSrvSlot);
            _freeSrvSlots.Push(composition.BlockedAreaDebugSrvSlot);
            _freeSrvSlots.Push(composition.TerrainTopologyDebugSrvSlot);
        }

        _pendingUploads.Clear();
    }

    public void Dispose()
    {
        StopWorker();
        foreach (var texture in _textures.Values)
        {
            texture.BaseResource.Dispose();
            texture.LiquidCoverResource.Dispose();
            texture.StairsDebugResource.Dispose();
            texture.BlockedAreaDebugResource.Dispose();
            texture.TerrainTopologyDebugResource.Dispose();
        }
        _textures.Clear();

        _compositionWorker.Dispose();
    }

    private void CollectCompletedUploads(Dx12FrameContext frame)
    {
        while (_compositionWorker.TryDequeueCompleted(out var composition))
        {
            _pendingUploads.Remove(composition.Coord);
            if (!IsWanted(composition.Composition))
            {
                composition.Composed?.Dispose();
                ReleaseSrvSlots(composition);
                continue;
            }

            if (composition.Error is not null)
            {
                ReleaseSrvSlots(composition);
                throw new InvalidOperationException(
                    $"Failed to compose sector texture {composition.Coord.X},{composition.Coord.Y}.",
                    composition.Error);
            }

            if (composition.Composed is null)
            {
                ReleaseSrvSlots(composition);
                continue;
            }

            var composed = composition.Composed;
            if (_textures.Remove(composition.Coord, out var existing))
                Retire(existing, frame);

            _uploader.CreateShaderResourceView(composed.BaseTexture, SrvCpuHandle(composition.BaseSrvSlot));
            _uploader.CreateShaderResourceView(composed.LiquidCoverTexture, SrvCpuHandle(composition.LiquidCoverSrvSlot));
            _uploader.CreateShaderResourceView(
                composed.StairsDebugTexture,
                SrvCpuHandle(composition.StairsDebugSrvSlot));
            _uploader.CreateShaderResourceView(
                composed.BlockedAreaDebugTexture,
                SrvCpuHandle(composition.BlockedAreaDebugSrvSlot));
            _uploader.CreateShaderResourceView(
                composed.TerrainTopologyDebugTexture,
                SrvCpuHandle(composition.TerrainTopologyDebugSrvSlot));
            _textures.Add(composition.Coord, new SectorTexture(
                composition.Composition,
                composed.BaseTexture,
                composed.LiquidCoverTexture,
                composed.StairsDebugTexture,
                composed.BlockedAreaDebugTexture,
                composed.TerrainTopologyDebugTexture,
                composition.BaseSrvSlot,
                composition.LiquidCoverSrvSlot,
                composition.StairsDebugSrvSlot,
                composition.BlockedAreaDebugSrvSlot,
                composition.TerrainTopologyDebugSrvSlot));
            EngineLog.WriteLine(
                $"Sector GPU texture loaded: {composition.Coord.X},{composition.Coord.Y}; " +
                $"embedded shadow receivers={composition.Composition.EmbeddedSpriteCount}.");
        }
    }

    private void QueueMissingUploads(IReadOnlyList<TerrainSectorComposition> images)
    {
        foreach (var image in images)
        {
            if (_textures.TryGetValue(image.Coord, out var existing) &&
                ReferenceEquals(existing.Composition, image))
                continue;

            if (_pendingUploads.Contains(image.Coord))
                continue;

            if (_freeSrvSlots.Count < TexturesPerSector)
                return;

            var baseSlot = _freeSrvSlots.Pop();
            var liquidCoverSlot = _freeSrvSlots.Pop();
            var stairsDebugSlot = _freeSrvSlots.Pop();
            var blockedAreaDebugSlot = _freeSrvSlots.Pop();
            var terrainTopologyDebugSlot = _freeSrvSlots.Pop();
            _pendingUploads.Add(image.Coord);
            if (_compositionWorker.TryEnqueue(new SectorCompositionRequest(
                    image,
                    baseSlot,
                    liquidCoverSlot,
                    stairsDebugSlot,
                    blockedAreaDebugSlot,
                    terrainTopologyDebugSlot,
                    _textures.ContainsKey(image.Coord),
                    _requestSequence++)))
                continue;

            _pendingUploads.Remove(image.Coord);
            _freeSrvSlots.Push(baseSlot);
            _freeSrvSlots.Push(liquidCoverSlot);
            _freeSrvSlots.Push(stairsDebugSlot);
            _freeSrvSlots.Push(blockedAreaDebugSlot);
            _freeSrvSlots.Push(terrainTopologyDebugSlot);
            return;
        }
    }

    private void UpdateWantedCompositions(IReadOnlyList<TerrainSectorComposition> images)
    {
        var current = Volatile.Read(ref _wantedCompositions);
        if (current.Count == images.Count)
        {
            var unchanged = true;
            foreach (var image in images)
            {
                if (current.TryGetValue(image.Coord, out var currentComposition) &&
                    ReferenceEquals(currentComposition, image))
                {
                    continue;
                }

                unchanged = false;
                break;
            }

            if (unchanged)
                return;
        }

        var wanted = new Dictionary<SectorCoord, TerrainSectorComposition>(images.Count);
        foreach (var image in images)
            wanted[image.Coord] = image;
        Volatile.Write(ref _wantedCompositions, wanted);
    }

    private bool IsWanted(TerrainSectorComposition composition)
    {
        var wantedCompositions = Volatile.Read(ref _wantedCompositions);
        return wantedCompositions.TryGetValue(composition.Coord, out var wanted) &&
               ReferenceEquals(wanted, composition);
    }

    private void RetireUnneededTextures(Dx12FrameContext frame)
    {
        _texturesToRetire.Clear();
        foreach (var pair in _textures)
            if (!IsWanted(pair.Value.Composition))
                _texturesToRetire.Add(pair.Key);

        foreach (var coord in _texturesToRetire)
        {
            var texture = _textures[coord];
            Retire(texture, frame);
            _textures.Remove(coord);
            EngineLog.WriteLine($"Sector GPU texture retiring asynchronously: {coord.X},{coord.Y}.");
        }
    }

    private void Retire(SectorTexture texture, Dx12FrameContext frame)
    {
        frame.RetireResource(texture.BaseResource);
        frame.RetireResource(texture.LiquidCoverResource);
        frame.RetireResource(texture.StairsDebugResource);
        frame.RetireResource(texture.BlockedAreaDebugResource);
        frame.RetireResource(texture.TerrainTopologyDebugResource);
        frame.RetireSectorSrvSlot(texture.BaseSrvSlot);
        frame.RetireSectorSrvSlot(texture.LiquidCoverSrvSlot);
        frame.RetireSectorSrvSlot(texture.StairsDebugSrvSlot);
        frame.RetireSectorSrvSlot(texture.BlockedAreaDebugSrvSlot);
        frame.RetireSectorSrvSlot(texture.TerrainTopologyDebugSrvSlot);
        _retiringSrvSlotCount += TexturesPerSector;
    }

    private CpuDescriptorHandle SrvCpuHandle(int index) => _srvHeapStart + index * _descriptorSize;

    private void ReleaseSrvSlots(SubmittedSectorComposition composition)
    {
        _freeSrvSlots.Push(composition.BaseSrvSlot);
        _freeSrvSlots.Push(composition.LiquidCoverSrvSlot);
        _freeSrvSlots.Push(composition.StairsDebugSrvSlot);
        _freeSrvSlots.Push(composition.BlockedAreaDebugSrvSlot);
        _freeSrvSlots.Push(composition.TerrainTopologyDebugSrvSlot);
    }

}
