using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene.InGame;
using Sacred.World.Geometry;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics.Sprites;

/// <summary>
/// Trims the engine's already composed Mixed.pak color textures using GPU copies.
/// No readback, upload, extra blending, or depth resources are needed. Original
/// textures remain the fallback for pending copies and for other sprite consumers.
/// </summary>
internal sealed class Dx12StaticCutoutCache(
    ID3D12Device device,
    ID3D12GraphicsCommandList commands,
    Dx12TextureUploader uploader,
    Stack<int> freeSlots,
    CpuDescriptorHandle heapStart,
    int descriptorSize,
    int firstSlot) : IDisposable
{
    private readonly Dictionary<StaticSpriteAsset, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<StaticSpriteAsset> _wanted = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<StaticSpriteAsset> _failed = new(ReferenceEqualityComparer.Instance);
    private readonly List<StaticSpriteAsset> _remove = [];
    private readonly List<StaticSpriteAsset> _requests = [];
    private int _cursor;
    private bool _enabled;
    private long _budgetBytes = 128L * 1024 * 1024;
    private long _committedBytes;
    private long _retiringBytes;
    private long _copies;
    private long _copyBytes;
    private double _preparationMs;
    private long _frame;
    private Vector2 _viewCenter;
    private float _zoom = 1;
    private int _renderWidth;
    private int _renderHeight;
    private bool _viewChanged = true;
    private ulong _spriteRevision = ulong.MaxValue;
    public ulong ReleasedSlotsRevision { get; private set; }

    public void SetView(SacredCamera camera, int width, int height)
    {
        _viewChanged |= _viewCenter != camera.ViewCenter || _zoom != camera.ViewportZoom ||
                        _renderWidth != width || _renderHeight != height;
        _viewCenter = camera.ViewCenter;
        _zoom = camera.ViewportZoom;
        _renderWidth = width;
        _renderHeight = height;
    }

    public Dx12GpuAnimationTimings? GpuTimings { get; set; }
    public ulong Revision { get; private set; }
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            _viewChanged = true;
            if (value) _failed.Clear();
            Revision++;
        }
    }
    public int BudgetMiB
    {
        get => (int)(_budgetBytes / (1024 * 1024));
        set
        {
            _budgetBytes = Math.Clamp(value, 0, 4096) * 1024L * 1024;
            _viewChanged = true;
        }
    }
    public string Status => $"static cutouts {(Enabled ? "on" : "off")}; cached {_entries.Count}; " +
        $"committed {_committedBytes} bytes (retiring {_retiringBytes}), budget {BudgetMiB} MiB; " +
        $"copies {_copies}, copied {_copyBytes} bytes, CPU preparation {_preparationMs:F3} ms total; upload 0 bytes";

    public void Prepare(
        IReadOnlyList<TerrainStaticSprite> sprites, Dx12FrameContext frame, ulong spriteRevision,
        Func<StaticSpriteAsset, ID3D12Resource?> findSource)
    {
        if (!Enabled && _entries.Count == 0) return;
        var start = Stopwatch.GetTimestamp();
        try
        {
            _frame++;
            if (_viewChanged || _spriteRevision != spriteRevision)
            {
                _viewChanged = false;
                _spriteRevision = spriteRevision;
                var transform = IsometricProjection.CreateScreenTransform(_viewCenter, _zoom, _renderWidth, _renderHeight);
                _wanted.Clear();
                if (Enabled && _budgetBytes > 0)
                foreach (var sprite in sprites)
                {
                    if (!Eligible(sprite)) continue;
                    var position = transform.ToScreen(sprite.IsoX, sprite.IsoY);
                    var width = transform.Scale(sprite.RenderWidth);
                    var height = transform.Scale(sprite.RenderHeight);
                    if (position.X >= _renderWidth || position.Y >= _renderHeight ||
                        position.X + width <= 0 || position.Y + height <= 0 ||
                        !StaticCutoutBinding.PreservesRasterMapping(sprite.Sprite, width, height)) continue;
                    _wanted.Add(sprite.Sprite);
                }
                _requests.Clear();
                foreach (var source in _wanted)
                    if (!_entries.ContainsKey(source) && !_failed.Contains(source)) _requests.Add(source);
                _cursor = 0;
            }

            _remove.Clear();
            foreach (var (source, entry) in _entries)
            {
                var wanted = _wanted.Contains(source);
                if (wanted) entry.LastUsed = _frame;
                if (!Enabled || _budgetBytes == 0 || freeSlots.Count < 128 || !wanted && _frame - entry.LastUsed > 120)
                    _remove.Add(source);
            }
            foreach (var source in _remove) Retire(source, frame);

            // Budget reductions retire entries before admitting new ones. Retiring
            // allocations remain charged until the existing frame fence completes.
            if (_committedBytes - _retiringBytes > _budgetBytes)
            {
                _remove.Clear();
                foreach (var source in _entries.Keys) _remove.Add(source);
                foreach (var source in _remove)
                {
                    if (_committedBytes - _retiringBytes <= _budgetBytes) break;
                    Retire(source, frame);
                }
            }
            if (!Enabled || _requests.Count == 0 || _budgetBytes == 0 || freeSlots.Count < 128) return;

            // Bound both command recording and allocation work. Continue scanning
            // across frames, including when the world revision changes every frame.
            var examined = 0;
            var copied = 0;
            var maximumExamined = Math.Min(128, _requests.Count);
            while (_requests.Count > 0 && examined++ < maximumExamined && copied < 2)
            {
                if (_cursor >= _requests.Count) _cursor = 0;
                var source = _requests[_cursor];
                if (findSource(source) is { } texture && TryCopy(source, texture))
                {
                    _requests.RemoveAt(_cursor);
                    copied++;
                }
                else if (_failed.Contains(source)) _requests.RemoveAt(_cursor);
                else _cursor++;
                if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 0.5) break;
            }
            if (copied > 0) EngineLog.WriteLine($"Static cutout copies recorded: {copied}; {_entries.Count} cached, {_committedBytes} committed bytes.");
        }
        finally { _preparationMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    }

    private static bool Eligible(TerrainStaticSprite sprite) =>
        !sprite.IsEmbeddedInTerrain && !sprite.IsParticleSprite && !sprite.TransposeTexture &&
        !sprite.UsesSpriteDepth &&
        (sprite.Sprite.FrameCount == 1 || sprite.Sprite.AnimationPeriodSeconds > 0) &&
        (sprite.Sprite.Coverage.Width < sprite.Sprite.Width || sprite.Sprite.Coverage.Height < sprite.Sprite.Height);

    private bool TryCopy(StaticSpriteAsset source, ID3D12Resource texture)
    {
        var bounds = source.Coverage;
        if (_budgetBytes - _committedBytes < 65536) return false;
        var description = texture.Description;
        description.Width = checked((ulong)(bounds.Width * source.AtlasColumns));
        description.Height = checked((uint)(bounds.Height * source.AtlasRows));
        var allocation = device.GetResourceAllocationInfo(0, description).SizeInBytes;
        if (allocation > (ulong)Math.Max(0, _budgetBytes - _committedBytes)) return false;

        ID3D12Resource target;
        try
        {
            target = device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                description, ResourceStates.CopyDest, null);
        }
        catch (Exception error)
        {
            _failed.Add(source);
            EngineLog.WriteLine($"Static cutout allocation failed for {source.GroupId}: {error.Message}");
            return false;
        }

        var slot = freeSlots.Pop();
        using (GpuTimings?.Measure(AnimationGpuStage.StaticCutoutCopies))
        {
            Dx12TextureUploader.Transition(commands, texture, ResourceStates.PixelShaderResource, ResourceStates.CopySource);
            for (var frame = 0; frame < source.FrameCount; frame++)
            {
                var column = frame % source.AtlasColumns;
                var row = frame / source.AtlasColumns;
                var left = column * source.Width + bounds.X;
                var top = row * source.Height + bounds.Y;
                commands.CopyTextureRegion(new TextureCopyLocation(target, 0),
                    (uint)(column * bounds.Width), (uint)(row * bounds.Height), 0,
                    new TextureCopyLocation(texture, 0),
                    new Box(left, top, 0, left + bounds.Width, top + bounds.Height, 1));
            }
            Dx12TextureUploader.Transition(commands, texture, ResourceStates.CopySource, ResourceStates.PixelShaderResource);
            Dx12TextureUploader.Transition(commands, target, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
        }
        uploader.CreateShaderResourceView(target, heapStart + slot * descriptorSize);
        _entries.Add(source, new Entry(target, slot, (long)allocation) { LastUsed = _frame });
        _committedBytes += (long)allocation;
        _copies++;
        _copyBytes += (long)bounds.Width * bounds.Height * source.FrameCount * 4;
        Revision++;
        return true;
    }

    public bool TryGet(TerrainStaticSprite sprite, float width, float height, out StaticCutoutBinding binding)
    {
        if (Enabled && Eligible(sprite) && _entries.TryGetValue(sprite.Sprite, out var entry) &&
            StaticCutoutBinding.PreservesRasterMapping(sprite.Sprite, width, height))
        {
            binding = StaticCutoutBinding.Create(sprite.Sprite, (uint)(entry.Slot - firstSlot));
            return true;
        }
        binding = default;
        return false;
    }

    private void Retire(StaticSpriteAsset source, Dx12FrameContext frame)
    {
        var entry = _entries[source];
        _entries.Remove(source);
        if (Enabled && _wanted.Contains(source) && !_requests.Contains(source)) _requests.Add(source);
        _retiringBytes += entry.Bytes;
        Revision++;
        frame.RetireAfterFence(() =>
        {
            entry.Texture.Dispose();
            freeSlots.Push(entry.Slot);
            _committedBytes -= entry.Bytes;
            _retiringBytes -= entry.Bytes;
            ReleasedSlotsRevision++;
        });
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values) entry.Texture.Dispose();
        _entries.Clear();
    }

    private sealed record Entry(ID3D12Resource Texture, int Slot, long Bytes)
    {
        public long LastUsed { get; set; }
    }
}

internal readonly record struct StaticCutoutBinding(uint Slot, Vector4 Rectangle)
{
    public static bool PreservesRasterMapping(StaticSpriteAsset source, float width, float height)
    {
        var bounds = source.Coverage;
        var inset = source.FrameCount > 1 ? 1 : 0;
        var scaleX = width / (double)Math.Max(1, source.Width - inset);
        var scaleY = height / (double)Math.Max(1, source.Height - inset);
        // D3D rasterizes vertices on a subpixel grid. Cropping at arbitrary scales
        // changes interpolation enough to cross alpha-test boundaries at a few
        // silhouette pixels. Keep the original quad unless all trim edges preserve
        // that grid, including the animated shader's texel-center mapping.
        return Aligned(width) && Aligned(height) && Aligned(bounds.X * scaleX) && Aligned(bounds.Y * scaleY) &&
               Aligned((bounds.Width - inset) * scaleX) && Aligned((bounds.Height - inset) * scaleY);

        static bool Aligned(double value) => Math.Abs(value * 256 - Math.Round(value * 256)) < 0.00001;
    }

    public static double VisibleArea(Vector2 position, float width, float height, int renderWidth, int renderHeight) =>
        Math.Max(0, Math.Min(renderWidth, position.X + width) - Math.Max(0, position.X)) *
        (double)Math.Max(0, Math.Min(renderHeight, position.Y + height) - Math.Max(0, position.Y));

    public static StaticCutoutBinding Create(StaticSpriteAsset source, uint slot)
    {
        var bounds = source.Coverage;
        // Animated atlas sampling maps UV 0..1 to first..last texel centers;
        // single-frame sampling maps to texel edges. Preserve that exact mapping.
        var inset = source.FrameCount > 1 ? 1 : 0;
        return new(slot, new Vector4(
            bounds.X / (float)Math.Max(1, source.Width - inset),
            bounds.Y / (float)Math.Max(1, source.Height - inset),
            (bounds.Width - inset) / (float)Math.Max(1, source.Width - inset),
            (bounds.Height - inset) / (float)Math.Max(1, source.Height - inset)));
    }
}
