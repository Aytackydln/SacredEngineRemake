using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.Uploads;
using Sacred.Engine.Rendering;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Sprites;

/// <summary>Stages requests and publishes completed copy-queue uploads without waiting.</summary>
internal sealed class Dx12SpriteTextureCache : IDisposable
{
    public const int MaximumTextureCount = 4096;
    private readonly Dx12TextureUploader _uploader;
    private readonly Dx12TextureUploadWorker _uploads;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly CpuDescriptorHandle _srvHeapStart;
    private readonly int _descriptorSize;
    private readonly int _firstSrvSlot;
    private readonly Stack<int> _freeSrvSlots = new(MaximumTextureCount);
    private readonly Dictionary<StaticSpriteAsset, SpriteTexture> _staticTextures = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, SpriteTexture> _liquidTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TextureFrameSequenceAsset, uint> _liquidSlotByAsset = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<StaticSpriteAsset> _failedStaticUploads = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _failedLiquidUploads = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<StaticSpriteAsset> _pendingStatic = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _pendingLiquid = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PendingSpriteUpload> _pending = [];
    private ulong _preparedSpriteRevision = ulong.MaxValue;
    private ulong _scanningSpriteRevision = ulong.MaxValue;
    private int _nextLiquid;
    private int _nextStatic;
    private bool _scanReady = true;
    private ulong _residencyRevision;
    private long _residentBytes;
    private long _uploadedBytes;
    private ulong _releasedCutoutSlotsRevision;
    public Dx12StaticCutoutCache Cutouts { get; }
    private readonly Func<StaticSpriteAsset, ID3D12Resource?> _findStaticResource;
    public string MemoryStatus => $"source sprite textures {_residentBytes} bytes committed; uploaded {_uploadedBytes} bytes";

    public Dx12SpriteTextureCache(ID3D12Device device, Dx12TextureUploader uploader, Dx12TextureUploadWorker uploads,
        ID3D12GraphicsCommandList commandList, ID3D12DescriptorHeap srvHeap, int descriptorSize, int firstSrvSlot)
    {
        _uploader = uploader;
        _uploads = uploads;
        _commandList = commandList;
        _srvHeapStart = srvHeap.GetCPUDescriptorHandleForHeapStart();
        _descriptorSize = descriptorSize;
        _firstSrvSlot = firstSrvSlot;
        _findStaticResource = FindStaticResource;
        for (var slot = firstSrvSlot + MaximumTextureCount - 1; slot >= firstSrvSlot; slot--)
            _freeSrvSlots.Push(slot);
        Cutouts = new(device, commandList, uploader, _freeSrvSlots, _srvHeapStart, descriptorSize, firstSrvSlot);
    }

    /// <summary>Sprites examined per residency pass (liquid and static share it). Each visit is a few dictionary probes,
    /// so this bounds a pass to a fraction of a millisecond on any CPU, deterministically.</summary>
    private const int MaximumScannedSpritesPerPass = 4096;
    /// <summary>Finished uploads turned into shader views per frame.</summary>
    private const int MaximumPublishedTexturesPerFrame = 16;

    public ulong ResidencyRevision => _residencyRevision + Cutouts.Revision;
    public bool IsPrepared(ulong revision) => _preparedSpriteRevision == revision;

    public void Prepare(IReadOnlyList<TerrainLiquidSprite> liquids, IReadOnlyList<TerrainStaticSprite> statics,
        Dx12FrameContext frame, ulong revision)
    {
        CollectCompleted();
        Cutouts.Prepare(statics, frame, revision, _findStaticResource);
        if (_releasedCutoutSlotsRevision != Cutouts.ReleasedSlotsRevision)
        {
            _releasedCutoutSlotsRevision = Cutouts.ReleasedSlotsRevision;
            _preparedSpriteRevision = ulong.MaxValue;
            RestartScan();
        }
        if (IsPrepared(revision)) return;
        if (_scanningSpriteRevision != revision)
        {
            _scanningSpriteRevision = revision;
            // Live particles revise the list every frame. Preserve our position so
            // their textures and newly visible buildings still get an upload turn.
            // A changed prefix is revisited on the next pass; this partial pass
            // cannot certify that the entire new revision is ready.
            _scanReady = false;
        }
        // Keep the cursor across frames so a long list is covered over several passes. The budget is a number of
        // sprites, not elapsed time: a clock cutoff made the covered prefix depend on CPU speed and load, so which
        // textures were requested first, and when the scan completed, was not reproducible.
        var scanned = 0;
        TextureFrameSequenceAsset? lastLiquid = null;
        var liquidSpan = ReadOnlyListSpan.Of(liquids);
        var nextLiquid = _nextLiquid;
        while (nextLiquid < liquidSpan.Length)
        {
            var source = liquidSpan[nextLiquid++].Animation;
            // A repeat of the previous asset cannot change the outcome: it is resident, failed, or already pending.
            if (!ReferenceEquals(source, lastLiquid) && !_liquidTextures.ContainsKey(source.Name) && !_failedLiquidUploads.Contains(source.Name) &&
                (_freeSrvSlots.Count > 0 || _pendingLiquid.Contains(source.Name)))
            {
                _scanReady = false;
                if (!_pendingLiquid.Contains(source.Name) && _freeSrvSlots.Count > 0)
                {
                    try
                    {
                        if (_uploads.TryEnqueue(source.Name, source.AtlasWidth, source.AtlasHeight, source.Rgba8FrameAtlas,
                                source.ReleasePixelData, out var ticket))
                        {
                            _pendingLiquid.Add(source.Name);
                            _pending.Add(new PendingSpriteUpload(null, source.Name, ticket, _freeSrvSlots.Pop()));
                        }
                    }
                    catch (Exception exception)
                    {
                        _failedLiquidUploads.Add(source.Name);
                        EngineLog.WriteLine($"Liquid upload failed: {source.Name}: {exception.Message}");
                    }
                }
            }
            lastLiquid = source;
            if (++scanned >= MaximumScannedSpritesPerPass)
            {
                _nextLiquid = nextLiquid;
                return;
            }
        }
        _nextLiquid = nextLiquid;
        var staticSpan = ReadOnlyListSpan.Of(statics);
        var nextStatic = _nextStatic;
        StaticSpriteAsset? lastSprite = null, lastShadow = null;
        var lastSpriteReady = true;
        var lastShadowReady = true;
        while (nextStatic < staticSpan.Length)
        {
            ref readonly var sprite = ref staticSpan[nextStatic++];
            if (!sprite.IsEmbeddedInTerrain)
            {
                if (!ReferenceEquals(sprite.Sprite, lastSprite))
                {
                    lastSprite = sprite.Sprite;
                    lastSpriteReady = RequestStatic(lastSprite);
                }
                _scanReady &= lastSpriteReady;
            }
            if (sprite.Shadow is { } shadow)
            {
                if (!ReferenceEquals(shadow.Atlas, lastShadow))
                {
                    lastShadow = shadow.Atlas;
                    lastShadowReady = RequestStatic(lastShadow);
                }
                _scanReady &= lastShadowReady;
            }
            if (++scanned >= MaximumScannedSpritesPerPass)
            {
                _nextStatic = nextStatic;
                return;
            }
        }
        _nextStatic = nextStatic;
        if (_scanReady)
        {
            _preparedSpriteRevision = revision;
            EngineLog.WriteLine($"Sprite GPU preparation completed: revision {revision}; " +
                $"{_staticTextures.Count} static and {_liquidTextures.Count} liquid textures resident.");
        }
        else RestartScan();
    }

    private void RestartScan()
    {
        _nextLiquid = 0;
        _nextStatic = 0;
        _scanReady = true;
    }

    public bool TryGetParticleSlot(StaticSpriteAsset source,out uint slot)
    {
        RequestStatic(source);
        var found=TryGetStaticSlot(source,out slot);
        if(found) slot+=(uint)_firstSrvSlot;
        return found;
    }

    private bool RequestStatic(StaticSpriteAsset source)
    {
        if (_staticTextures.ContainsKey(source) || _failedStaticUploads.Contains(source)) return true;
        if (_pendingStatic.Contains(source)) return false;
        if (_freeSrvSlots.Count == 0) return true;
        try
        {
            if (_uploads.TryEnqueue($"sprite:{source.GroupId}", source.AtlasWidth, source.AtlasHeight,
                    source.Rgba, source.ReleasePixelData, out var ticket))
            {
                _pendingStatic.Add(source);
                _pending.Add(new PendingSpriteUpload(source, null, ticket, _freeSrvSlots.Pop()));
            }
        }
        catch (Exception exception)
        {
            _failedStaticUploads.Add(source);
            EngineLog.WriteLine($"Sprite upload failed: {source.GroupId}: {exception.Message}");
        }
        return false;
    }

    private void CollectCompleted()
    {
        var published = 0;
        for (var index = _pending.Count - 1; index >= 0 && published < MaximumPublishedTexturesPerFrame; index--)
        {
            var pending = _pending[index];
            if (!pending.Ticket.TryTake(out var resource, out var error)) continue;
            if (pending.StaticSource is { } source) _pendingStatic.Remove(source);
            else _pendingLiquid.Remove(pending.LiquidName!);
            _pending.RemoveAt(index);
            if (error is not null || resource is null)
            {
                _freeSrvSlots.Push(pending.Slot);
                if (pending.StaticSource is { } failed) _failedStaticUploads.Add(failed);
                else _failedLiquidUploads.Add(pending.LiquidName!);
                EngineLog.WriteLine($"Sprite copy failed: {pending.Ticket.Name}: {error?.Message}");
                continue;
            }
            Dx12TextureUploader.Transition(_commandList, resource, ResourceStates.Common, ResourceStates.PixelShaderResource);
            _uploader.CreateShaderResourceView(resource, SrvCpuHandle(pending.Slot));
            var texture = new SpriteTexture(resource, pending.Slot);
            if (pending.StaticSource is { } active) _staticTextures.Add(active, texture);
            else _liquidTextures.Add(pending.LiquidName!, texture);
            _residencyRevision++;
            var description = resource.Description;
            _residentBytes += (long)_uploader.GetAllocationBytes(resource);
            _uploadedBytes += (long)description.Width * description.Height * 4;
            published++;
        }
        if (published > 0)
            EngineLog.WriteLine($"Sprite GPU textures ready: {published}; {_pending.Count} awaiting copies.");
    }

    public bool TryGetStaticSlot(StaticSpriteAsset source, out uint slot)
    {
        var found = _staticTextures.TryGetValue(source, out var texture);
        slot = found ? (uint)(texture!.SrvSlot - _firstSrvSlot) : 0;
        return found;
    }
    private ID3D12Resource? FindStaticResource(StaticSpriteAsset source) =>
        _staticTextures.TryGetValue(source, out var texture) ? texture.Resource : null;
    /// <summary>Per-sprite lookup by asset instance. Resident liquid textures keep their slot until disposal, so the
    /// first name lookup is remembered and later reads skip hashing the name (case-insensitively) for every sprite.</summary>
    public bool TryGetLiquidSlot(TextureFrameSequenceAsset animation, out uint slot)
    {
        if (_liquidSlotByAsset.TryGetValue(animation, out slot))
            return true;
        if (!TryGetLiquidSlot(animation.Name, out slot))
            return false;
        _liquidSlotByAsset[animation] = slot;
        return true;
    }

    public bool TryGetLiquidSlot(string name, out uint slot)
    {
        var found = _liquidTextures.TryGetValue(name, out var texture);
        slot = found ? (uint)(texture!.SrvSlot - _firstSrvSlot) : 0;
        return found;
    }
    private CpuDescriptorHandle SrvCpuHandle(int slot) => _srvHeapStart + slot * _descriptorSize;

    public void Dispose()
    {
        Cutouts.Dispose();
        foreach (var pending in _pending) pending.Ticket.Cancel();
        _pending.Clear();
        foreach (var texture in _staticTextures.Values) texture.Resource.Dispose();
        foreach (var texture in _liquidTextures.Values) texture.Resource.Dispose();
        _staticTextures.Clear();
        _liquidTextures.Clear();
        _liquidSlotByAsset.Clear();
    }
    private sealed record SpriteTexture(ID3D12Resource Resource, int SrvSlot);
    private sealed record PendingSpriteUpload(StaticSpriteAsset? StaticSource, string? LiquidName, TextureUploadTicket Ticket, int Slot);
}
