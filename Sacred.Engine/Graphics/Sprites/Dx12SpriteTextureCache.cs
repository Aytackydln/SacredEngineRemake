using System;
using System.Collections.Generic;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Rendering;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Sprites;

/// <summary>Maintains persistent sprite texture residency and incrementally uploads new visible assets.</summary>
internal sealed class Dx12SpriteTextureCache : IDisposable
{
    public const int MaximumTextureCount = 4096;

    private const int StaticUploadBatchSize = 32;
    private const int LiquidUploadBatchSize = 2;

    private readonly Dx12TextureUploader _uploader;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly CpuDescriptorHandle _srvHeapStart;
    private readonly int _descriptorSize;
    private readonly int _firstSrvSlot;
    private readonly Stack<int> _freeSrvSlots = new(MaximumTextureCount);
    private readonly Dictionary<StaticSpriteAsset, SpriteTexture> _staticTextures =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, SpriteTexture> _liquidTextures =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<StaticSpriteAsset> _failedStaticUploads =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _failedLiquidUploads = new(StringComparer.OrdinalIgnoreCase);

    private ulong _preparedSpriteRevision = ulong.MaxValue;

    public Dx12SpriteTextureCache(
        Dx12TextureUploader uploader,
        ID3D12GraphicsCommandList commandList,
        ID3D12DescriptorHeap srvHeap,
        int descriptorSize,
        int firstSrvSlot)
    {
        _uploader = uploader;
        _commandList = commandList;
        _srvHeapStart = srvHeap.GetCPUDescriptorHandleForHeapStart();
        _descriptorSize = descriptorSize;
        _firstSrvSlot = firstSrvSlot;

        for (var index = firstSrvSlot + MaximumTextureCount - 1; index >= firstSrvSlot; index--)
            _freeSrvSlots.Push(index);
    }

    public ulong ResidencyRevision { get; private set; }

    public void Prepare(
        IReadOnlyList<TerrainLiquidSprite> liquidSprites,
        IReadOnlyList<TerrainStaticSprite> staticSprites,
        Dx12FrameContext frame,
        ulong spriteRevision)
    {
        if (_preparedSpriteRevision == spriteRevision)
            return;

        var liquidsReady = PrepareLiquidTextures(liquidSprites, frame);
        var staticsReady = PrepareStaticTextures(staticSprites, frame);
        if (liquidsReady && staticsReady)
            _preparedSpriteRevision = spriteRevision;
    }

    public bool TryGetStaticSlot(StaticSpriteAsset sprite, out uint relativeSlot)
    {
        if (_staticTextures.TryGetValue(sprite, out var texture))
        {
            relativeSlot = (uint)(texture.SrvSlot - _firstSrvSlot);
            return true;
        }

        relativeSlot = 0;
        return false;
    }

    public bool TryGetLiquidSlot(string animationName, out uint relativeSlot)
    {
        if (_liquidTextures.TryGetValue(animationName, out var texture))
        {
            relativeSlot = (uint)(texture.SrvSlot - _firstSrvSlot);
            return true;
        }

        relativeSlot = 0;
        return false;
    }

    public void Dispose()
    {
        foreach (var texture in _staticTextures.Values)
            texture.Resource.Dispose();
        _staticTextures.Clear();

        foreach (var texture in _liquidTextures.Values)
            texture.Resource.Dispose();
        _liquidTextures.Clear();
    }

    private bool PrepareLiquidTextures(IReadOnlyList<TerrainLiquidSprite> sprites, Dx12FrameContext frame)
    {
        if (_freeSrvSlots.Count == 0)
            return true;

        var attempted = 0;
        foreach (var visibleSprite in sprites)
        {
            var animation = visibleSprite.Animation;
            if (_liquidTextures.ContainsKey(animation.Name) ||
                _failedLiquidUploads.Contains(animation.Name) ||
                _freeSrvSlots.Count == 0)
            {
                continue;
            }

            var slot = _freeSrvSlots.Pop();
            ID3D12Resource? resource = null;
            try
            {
                resource = _uploader.UploadRgbaTexture(
                    _commandList,
                    animation.AtlasWidth,
                    animation.AtlasHeight,
                    animation.Rgba8FrameAtlas,
                    frame.TransientResources);
                _uploader.CreateShaderResourceView(resource, SrvCpuHandle(slot));
                _liquidTextures[animation.Name] = new SpriteTexture(resource, slot);
                ResidencyRevision++;
            }
            catch
            {
                resource?.Dispose();
                _failedLiquidUploads.Add(animation.Name);
                _freeSrvSlots.Push(slot);
            }

            if (++attempted == LiquidUploadBatchSize)
                return false;
        }

        return true;
    }

    private bool PrepareStaticTextures(IReadOnlyList<TerrainStaticSprite> sprites, Dx12FrameContext frame)
    {
        if (_freeSrvSlots.Count == 0)
            return true;

        var attempted = 0;
        foreach (var visibleSprite in sprites)
        {
            var sprite = visibleSprite.Sprite;
            if (_staticTextures.ContainsKey(sprite) ||
                _failedStaticUploads.Contains(sprite) ||
                _freeSrvSlots.Count == 0)
            {
                continue;
            }

            var slot = _freeSrvSlots.Pop();
            ID3D12Resource? resource = null;
            try
            {
                resource = _uploader.UploadRgbaTexture(
                    _commandList,
                    sprite.AtlasWidth,
                    sprite.AtlasHeight,
                    sprite.Rgba,
                    frame.TransientResources);
                _uploader.CreateShaderResourceView(resource, SrvCpuHandle(slot));
                _staticTextures[sprite] = new SpriteTexture(resource, slot);
                ResidencyRevision++;
            }
            catch
            {
                resource?.Dispose();
                _failedStaticUploads.Add(sprite);
                _freeSrvSlots.Push(slot);
            }

            if (++attempted == StaticUploadBatchSize)
                return false;
        }

        return true;
    }

    private CpuDescriptorHandle SrvCpuHandle(int index) => _srvHeapStart + index * _descriptorSize;

    private sealed record SpriteTexture(ID3D12Resource Resource, int SrvSlot);
}
