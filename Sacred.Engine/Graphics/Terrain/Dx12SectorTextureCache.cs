using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Sacred.Core.World.Sector;
using Sacred.Engine.Extern;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Rendering;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Terrain;

/// <summary>Owns the bounded, fence-safe GPU cache and dedicated sector upload queue.</summary>
internal sealed class Dx12SectorTextureCache : IDisposable
{
    private readonly int _maximumTextureCount;
    private readonly Dx12TextureUploader _uploader;
    private readonly CpuDescriptorHandle _srvHeapStart;
    private readonly int _descriptorSize;
    private readonly Dictionary<SectorCoord, SectorTexture> _textures = new();
    private readonly HashSet<SectorCoord> _pendingUploads = [];
    private readonly BlockingCollection<SectorUploadRequest> _uploadRequests = new();
    private readonly ConcurrentQueue<SubmittedSectorUpload> _submittedUploads = new();
    private readonly ID3D12CommandQueue _uploadCommandQueue;
    private readonly ID3D12Fence _uploadFence;
    private readonly Stack<int> _freeSrvSlots;

    private Thread? _uploadThread;
    private nint _uploadFenceEvent;
    private ulong _uploadFenceValue;
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
        _srvHeapStart = srvHeap.GetCPUDescriptorHandleForHeapStart();
        _descriptorSize = descriptorSize;
        _freeSrvSlots = new Stack<int>(maximumTextureCount * 2);
        for (var index = maximumTextureCount * 2 - 1; index >= 0; index--)
            _freeSrvSlots.Push(index);

        _uploadCommandQueue = device.CreateCommandQueue(CommandListType.Direct);
        _uploadFence = device.CreateFence(0, FenceFlags.None);
        _uploadFenceEvent = Kernel32.CreateEventA(IntPtr.Zero, false, false, null);
        if (_uploadFenceEvent == 0)
            throw new InvalidOperationException("Failed to create the sector-upload fence event.");

        _uploadThread = new Thread(UploadWorkerLoop)
        {
            IsBackground = true,
            Name = "Sacred sector texture uploader"
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
        IReadOnlyList<TerrainSectorImage> images,
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
        _uploadRequests.CompleteAdding();
        _uploadThread?.Join();
        _uploadThread = null;

        WaitForSubmittedUploads();
        while (_submittedUploads.TryDequeue(out var upload))
        {
            upload.Upload?.Dispose();
            _pendingUploads.Remove(upload.Coord);
            _freeSrvSlots.Push(upload.BaseSrvSlot);
            _freeSrvSlots.Push(upload.LiquidCoverSrvSlot);
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

        _uploadRequests.Dispose();
        _uploadFence.Dispose();
        _uploadCommandQueue.Dispose();
        if (_uploadFenceEvent != 0)
        {
            Kernel32.CloseHandle(_uploadFenceEvent);
            _uploadFenceEvent = 0;
        }
    }

    private void CollectCompletedUploads(Dx12FrameContext frame, ulong frameId)
    {
        while (_submittedUploads.TryPeek(out var pending))
        {
            if (pending.Upload is { } pendingUpload && _uploadFence.CompletedValue < pendingUpload.FenceValue)
                break;

            if (!_submittedUploads.TryDequeue(out var upload))
                break;
            _pendingUploads.Remove(upload.Coord);
            if (upload.Error is not null)
            {
                _freeSrvSlots.Push(upload.BaseSrvSlot);
                _freeSrvSlots.Push(upload.LiquidCoverSrvSlot);
                throw new InvalidOperationException(
                    $"Failed to upload sector texture {upload.Coord.X},{upload.Coord.Y}.",
                    upload.Error);
            }

            if (upload.Upload is null)
            {
                _freeSrvSlots.Push(upload.BaseSrvSlot);
                _freeSrvSlots.Push(upload.LiquidCoverSrvSlot);
                continue;
            }

            var submitted = upload.Upload;
            submitted.ReleaseCompletedUploadResources();
            if (_textures.Remove(upload.Coord, out var existing))
                Retire(existing, frame);

            _uploader.CreateShaderResourceView(submitted.BaseTexture, SrvCpuHandle(upload.BaseSrvSlot));
            _uploader.CreateShaderResourceView(submitted.LiquidCoverTexture, SrvCpuHandle(upload.LiquidCoverSrvSlot));
            _textures.Add(upload.Coord, new SectorTexture(
                submitted.BaseTexture,
                submitted.LiquidCoverTexture,
                upload.BaseSrvSlot,
                upload.LiquidCoverSrvSlot,
                frameId));
        }
    }

    private void QueueMissingUploads(IReadOnlyList<TerrainSectorImage> images, Dx12FrameContext frame)
    {
        foreach (var image in images)
        {
            if (_textures.ContainsKey(image.Coord))
            {
                if (image.HasCpuPixels)
                    image.ReleaseCpuPixels();
                continue;
            }

            if (_pendingUploads.Contains(image.Coord) || !image.HasCpuPixels)
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
            if (_uploadRequests.TryAdd(new SectorUploadRequest(image, baseSlot, liquidCoverSlot)))
                continue;

            _pendingUploads.Remove(image.Coord);
            _freeSrvSlots.Push(baseSlot);
            _freeSrvSlots.Push(liquidCoverSlot);
            return;
        }
    }

    private void TouchVisibleTextures(IReadOnlyList<TerrainSectorImage> images, ulong frameId)
    {
        foreach (var image in images)
            if (_textures.TryGetValue(image.Coord, out var texture))
                texture.LastUsedFrame = frameId;
    }

    private void EvictLeastRecentlyUsed(IReadOnlyList<TerrainSectorImage> visibleImages, Dx12FrameContext frame)
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
        foreach (var request in _uploadRequests.GetConsumingEnumerable())
            _submittedUploads.Enqueue(Upload(request));
    }

    private SubmittedSectorUpload Upload(SectorUploadRequest request)
    {
        try
        {
            var image = request.Image;
            var upload = _uploader.SubmitSectorTextures(
                _uploadCommandQueue,
                _uploadFence,
                ref _uploadFenceValue,
                image.Width,
                image.Height,
                image.GetCpuPixels(),
                image.GetLiquidCoverCpuPixels());
            image.ReleaseCpuPixels();
            return new SubmittedSectorUpload(
                image.Coord,
                upload,
                request.BaseSrvSlot,
                request.LiquidCoverSrvSlot,
                null);
        }
        catch (Exception exception)
        {
            return new SubmittedSectorUpload(
                request.Image.Coord,
                null,
                request.BaseSrvSlot,
                request.LiquidCoverSrvSlot,
                exception);
        }
    }

    private CpuDescriptorHandle SrvCpuHandle(int index) => _srvHeapStart + index * _descriptorSize;

    private sealed record SectorUploadRequest(TerrainSectorImage Image, int BaseSrvSlot, int LiquidCoverSrvSlot);

    private void WaitForSubmittedUploads()
    {
        var fenceValue = _uploadFenceValue;
        if (fenceValue == 0 || _uploadFence.CompletedValue >= fenceValue)
            return;

        _uploadFence.SetEventOnCompletion(fenceValue, _uploadFenceEvent).CheckError();
        Kernel32.WaitForSingleObject(_uploadFenceEvent, uint.MaxValue);
    }

    private sealed record SubmittedSectorUpload(
        SectorCoord Coord,
        Dx12SectorTextureUpload? Upload,
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
