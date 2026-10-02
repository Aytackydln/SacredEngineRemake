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

    public Dx12SpriteTextureCache(Dx12TextureUploader uploader, Dx12TextureUploadWorker uploads,
        ID3D12GraphicsCommandList commandList, ID3D12DescriptorHeap srvHeap, int descriptorSize, int firstSrvSlot)
    {
        _uploader = uploader;
        _uploads = uploads;
        _commandList = commandList;
        _srvHeapStart = srvHeap.GetCPUDescriptorHandleForHeapStart();
        _descriptorSize = descriptorSize;
        _firstSrvSlot = firstSrvSlot;
        for (var slot = firstSrvSlot + MaximumTextureCount - 1; slot >= firstSrvSlot; slot--)
            _freeSrvSlots.Push(slot);
    }

    public ulong ResidencyRevision { get; private set; }
    public bool IsPrepared(ulong revision) => _preparedSpriteRevision == revision;

    public void Prepare(IReadOnlyList<TerrainLiquidSprite> liquids, IReadOnlyList<TerrainStaticSprite> statics,
        Dx12FrameContext frame, ulong revision)
    {
        CollectCompleted();
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
        var timer = Stopwatch.StartNew();
        // Keep the cursor across frames. Restarting a timed scan at zero can permanently
        // starve its tail on slower CPUs, even when every texture in the prefix is resident.
        while (_nextLiquid < liquids.Count)
        {
            var source = liquids[_nextLiquid++].Animation;
            if (!_liquidTextures.ContainsKey(source.Name) && !_failedLiquidUploads.Contains(source.Name) &&
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
            if (timer.Elapsed.TotalMilliseconds >= 0.5) return;
        }
        while (_nextStatic < statics.Count)
        {
            var sprite = statics[_nextStatic++];
            if (!sprite.IsEmbeddedInTerrain) _scanReady &= RequestStatic(sprite.Sprite);
            if (sprite.Shadow is { } shadow) _scanReady &= RequestStatic(shadow.Atlas);
            if (timer.Elapsed.TotalMilliseconds >= 0.5) return;
        }
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
        var timer = Stopwatch.StartNew();
        var published = 0;
        for (var index = _pending.Count - 1; index >= 0 && timer.Elapsed.TotalMilliseconds < 0.5; index--)
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
            ResidencyRevision++;
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
    public bool TryGetLiquidSlot(string name, out uint slot)
    {
        var found = _liquidTextures.TryGetValue(name, out var texture);
        slot = found ? (uint)(texture!.SrvSlot - _firstSrvSlot) : 0;
        return found;
    }
    private CpuDescriptorHandle SrvCpuHandle(int slot) => _srvHeapStart + slot * _descriptorSize;

    public void Dispose()
    {
        foreach (var pending in _pending) pending.Ticket.Cancel();
        _pending.Clear();
        foreach (var texture in _staticTextures.Values) texture.Resource.Dispose();
        foreach (var texture in _liquidTextures.Values) texture.Resource.Dispose();
        _staticTextures.Clear();
        _liquidTextures.Clear();
    }
    private sealed record SpriteTexture(ID3D12Resource Resource, int SrvSlot);
    private sealed record PendingSpriteUpload(StaticSpriteAsset? StaticSource, string? LiquidName, TextureUploadTicket Ticket, int Slot);
}
