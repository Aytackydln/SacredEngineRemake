using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Sacred.Core.World.Sector;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Rendering;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Terrain;

/// <summary>Owns the bounded, fence-safe GPU cache and dedicated sector-composition queue.</summary>
internal sealed class Dx12SectorTextureCache : IDisposable
{
    private readonly int _maximumTextureCount;
    private readonly Dx12TextureUploader _uploader;
    private readonly Dx12SectorComposer _composer;
    private readonly CpuDescriptorHandle _srvHeapStart;
    private readonly int _descriptorSize;
    private readonly Dictionary<SectorCoord, SectorTexture> _textures = new();
    private readonly HashSet<SectorCoord> _pendingUploads = [];
    private readonly BlockingCollection<SectorCompositionRequest> _compositionRequests = new();
    private readonly ConcurrentQueue<SubmittedSectorComposition> _submittedCompositions = new();
    private readonly Stack<int> _freeSrvSlots;

    private Thread? _uploadThread;
    private int _retiringSrvSlotCount;
    private bool _stopped;

    public Dx12SectorTextureCache(
        ID3D12Device device,
        Dx12TextureUploader uploader,
        ID3D12DescriptorHeap srvHeap,
        int descriptorSize,
        int maximumTextureCount)
    {
        _maximumTextureCount = maximumTextureCount;
        _uploader = uploader;
        _composer = new Dx12SectorComposer(device, uploader);
        _srvHeapStart = srvHeap.GetCPUDescriptorHandleForHeapStart();
        _descriptorSize = descriptorSize;
        _freeSrvSlots = new Stack<int>(maximumTextureCount * 2);
        for (var index = maximumTextureCount * 2 - 1; index >= 0; index--)
            _freeSrvSlots.Push(index);

        _uploadThread = new Thread(UploadWorkerLoop)
        {
            IsBackground = true,
            Name = "Sacred GPU sector compositor"
        };
        _uploadThread.Start();
    }

    public int Count => _textures.Count;
    public int PendingUploadCount => _pendingUploads.Count;
    public int MaximumTextureCount => _maximumTextureCount;
    public Stack<int> FreeSrvSlots => _freeSrvSlots;

    public void OnFrameRetired(int releasedSectorSlotCount)
    {
        _retiringSrvSlotCount -= releasedSectorSlotCount;
    }

    public void PrepareFrame(
        IReadOnlyList<TerrainSectorComposition> images,
        Dx12FrameContext frame,
        ulong frameId)
    {
        CollectCompletedUploads(frame, frameId);
        TouchVisibleTextures(images, frameId);
        QueueMissingUploads(images, frame);
    }

    public bool TryGet(SectorCoord coord, out SectorTextureView texture)
    {
        if (_textures.TryGetValue(coord, out var cached))
        {
            texture = new SectorTextureView(cached.BaseSrvSlot, cached.LiquidCoverSrvSlot);
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
        _compositionRequests.CompleteAdding();
        _uploadThread?.Join();
        _uploadThread = null;

        while (_submittedCompositions.TryDequeue(out var composition))
        {
            composition.Composed?.Dispose();
            _pendingUploads.Remove(composition.Coord);
            _freeSrvSlots.Push(composition.BaseSrvSlot);
            _freeSrvSlots.Push(composition.LiquidCoverSrvSlot);
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
        }
        _textures.Clear();

        _compositionRequests.Dispose();
        _composer.Dispose();
    }

    private void CollectCompletedUploads(Dx12FrameContext frame, ulong frameId)
    {
        while (_submittedCompositions.TryDequeue(out var composition))
        {
            _pendingUploads.Remove(composition.Coord);
            if (composition.Error is not null)
            {
                _freeSrvSlots.Push(composition.BaseSrvSlot);
                _freeSrvSlots.Push(composition.LiquidCoverSrvSlot);
                throw new InvalidOperationException(
                    $"Failed to compose sector texture {composition.Coord.X},{composition.Coord.Y}.",
                    composition.Error);
            }

            if (composition.Composed is null)
            {
                _freeSrvSlots.Push(composition.BaseSrvSlot);
                _freeSrvSlots.Push(composition.LiquidCoverSrvSlot);
                continue;
            }

            var composed = composition.Composed;
            if (_textures.Remove(composition.Coord, out var existing))
                Retire(existing, frame);

            _uploader.CreateShaderResourceView(composed.BaseTexture, SrvCpuHandle(composition.BaseSrvSlot));
            _uploader.CreateShaderResourceView(composed.LiquidCoverTexture, SrvCpuHandle(composition.LiquidCoverSrvSlot));
            _textures.Add(composition.Coord, new SectorTexture(
                composed.BaseTexture,
                composed.LiquidCoverTexture,
                composition.BaseSrvSlot,
                composition.LiquidCoverSrvSlot,
                frameId));
        }
    }

    private void QueueMissingUploads(IReadOnlyList<TerrainSectorComposition> images, Dx12FrameContext frame)
    {
        foreach (var image in images)
        {
            if (_textures.ContainsKey(image.Coord))
                continue;

            if (_pendingUploads.Contains(image.Coord))
                continue;

            if (_freeSrvSlots.Count < 2)
            {
                if (_retiringSrvSlotCount == 0)
                    EvictLeastRecentlyUsed(images, frame);
                if (_freeSrvSlots.Count < 2)
                    return;
            }

            var baseSlot = _freeSrvSlots.Pop();
            var liquidCoverSlot = _freeSrvSlots.Pop();
            _pendingUploads.Add(image.Coord);
            if (_compositionRequests.TryAdd(new SectorCompositionRequest(image, baseSlot, liquidCoverSlot)))
                continue;

            _pendingUploads.Remove(image.Coord);
            _freeSrvSlots.Push(baseSlot);
            _freeSrvSlots.Push(liquidCoverSlot);
            return;
        }
    }

    private void TouchVisibleTextures(IReadOnlyList<TerrainSectorComposition> images, ulong frameId)
    {
        foreach (var image in images)
            if (_textures.TryGetValue(image.Coord, out var texture))
                texture.LastUsedFrame = frameId;
    }

    private void EvictLeastRecentlyUsed(IReadOnlyList<TerrainSectorComposition> visibleImages, Dx12FrameContext frame)
    {
        SectorCoord? victimCoord = null;
        SectorTexture? victim = null;
        foreach (var pair in _textures)
        {
            var visible = false;
            foreach (var image in visibleImages)
            {
                if (image.Coord != pair.Key)
                    continue;
                visible = true;
                break;
            }

            if (!visible && (victim is null || pair.Value.LastUsedFrame < victim.LastUsedFrame))
            {
                victimCoord = pair.Key;
                victim = pair.Value;
            }
        }

        if (victimCoord is null || victim is null)
            return;

        Retire(victim, frame);
        _textures.Remove(victimCoord.Value);
    }

    private void Retire(SectorTexture texture, Dx12FrameContext frame)
    {
        frame.RetireResource(texture.BaseResource);
        frame.RetireResource(texture.LiquidCoverResource);
        frame.RetireSectorSrvSlot(texture.BaseSrvSlot);
        frame.RetireSectorSrvSlot(texture.LiquidCoverSrvSlot);
        _retiringSrvSlotCount += 2;
    }

    private void UploadWorkerLoop()
    {
        foreach (var request in _compositionRequests.GetConsumingEnumerable())
            _submittedCompositions.Enqueue(Compose(request));
    }

    private SubmittedSectorComposition Compose(SectorCompositionRequest request)
    {
        try
        {
            var composed = _composer.Compose(request.Composition);
            return new SubmittedSectorComposition(
                request.Composition.Coord,
                composed,
                request.BaseSrvSlot,
                request.LiquidCoverSrvSlot,
                null);
        }
        catch (Exception exception)
        {
            return new SubmittedSectorComposition(
                request.Composition.Coord,
                null,
                request.BaseSrvSlot,
                request.LiquidCoverSrvSlot,
                exception);
        }
    }

    private CpuDescriptorHandle SrvCpuHandle(int index) => _srvHeapStart + index * _descriptorSize;

    private sealed record SectorCompositionRequest(
        TerrainSectorComposition Composition,
        int BaseSrvSlot,
        int LiquidCoverSrvSlot);

    private sealed record SubmittedSectorComposition(
        SectorCoord Coord,
        Dx12ComposedSector? Composed,
        int BaseSrvSlot,
        int LiquidCoverSrvSlot,
        Exception? Error);

    private sealed class SectorTexture(
        ID3D12Resource baseResource,
        ID3D12Resource liquidCoverResource,
        int baseSrvSlot,
        int liquidCoverSrvSlot,
        ulong lastUsedFrame)
    {
        public ID3D12Resource BaseResource { get; } = baseResource;
        public ID3D12Resource LiquidCoverResource { get; } = liquidCoverResource;
        public int BaseSrvSlot { get; } = baseSrvSlot;
        public int LiquidCoverSrvSlot { get; } = liquidCoverSrvSlot;
        public ulong LastUsedFrame { get; set; } = lastUsedFrame;
    }
}

internal readonly record struct SectorTextureView(int BaseSrvSlot, int LiquidCoverSrvSlot);
