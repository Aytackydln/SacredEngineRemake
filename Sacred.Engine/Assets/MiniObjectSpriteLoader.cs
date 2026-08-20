using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;
using Sacred.World.Particles;

namespace Sacred.Engine.Assets;

/// <summary>
/// Loads atlas-backed world decals by their Items.pak Texture.pak entry ID.
/// Static.pak supplies the region or animation parameters.
/// </summary>
internal sealed class MiniObjectSpriteLoader
{
    private readonly Func<uint, Task<TextureAsset>> _loadTextureAsync;
    private readonly WorldSpriteLoadQueue _loadQueue;
    private readonly Dictionary<MiniObjectTextureReference, StaticSpriteAsset?> _sprites = [];
    private readonly HashSet<MiniObjectTextureReference> _loads = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public MiniObjectSpriteLoader(
        Func<uint, Task<TextureAsset>> loadTextureAsync,
        WorldSpriteLoadQueue loadQueue)
    {
        _loadTextureAsync = loadTextureAsync;
        _loadQueue = loadQueue;
    }

    public bool TryGetOrRequest(
        ItemsPakEntry item,
        byte sourceX,
        byte sourceY,
        byte sourceSize,
        byte animationFrameDurationTicks,
        byte animationFrameCount,
        out StaticSpriteAsset? sprite)
    {
        sprite = null;
        if (!WorldParticleMapper.TryResolveMiniObject(
                item,
                sourceX,
                sourceY,
                sourceSize,
                animationFrameDurationTicks,
                animationFrameCount,
                out var key))
            return true;

        if (!_lock.Wait(0))
            return false;

        try
        {
            if (_sprites.TryGetValue(key, out sprite))
                return true;

            if (_loads.Add(key))
                _loadQueue.Enqueue(() => LoadAndCacheAsync(key));

            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Clear()
    {
        _lock.Wait();
        try
        {
            _sprites.Clear();
            _loads.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<StaticSpriteAsset?> LoadAndCacheAsync(MiniObjectTextureReference key)
    {
        StaticSpriteAsset? sprite;
        try
        {
            var atlas = await _loadTextureAsync(key.TextureId).ConfigureAwait(false);
            sprite = BuildSprite(atlas, key);
            if (key.FrameCount > 1)
            {
                EngineLog.WriteLine(sprite is null
                    ? $"Animated mini-object atlas rejected: texture #{key.TextureId} ({key.AtlasColumns}x{key.AtlasRows}, {key.FrameCount} frames)."
                    : $"Animated mini-object atlas loaded: {atlas.Name} (#{key.TextureId}, {key.AtlasColumns}x{key.AtlasRows}, {key.FrameCount} frames).");
            }
        }
        catch (Exception exception)
        {
            sprite = null;
            EngineLog.WriteLine($"Mini-object atlas failed: texture #{key.TextureId}: {exception.Message}");
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _sprites[key] = sprite;
            _loads.Remove(key);
        }
        finally
        {
            _lock.Release();
        }

        return sprite;
    }

    private static StaticSpriteAsset? BuildSprite(TextureAsset atlas, MiniObjectTextureReference key)
    {
        if (key.FrameCount > 0)
        {
            if (key.AtlasColumns <= 0 || key.AtlasRows <= 0 ||
                key.FrameCount > key.AtlasColumns * key.AtlasRows ||
                atlas.Width % key.AtlasColumns != 0 ||
                atlas.Height % key.AtlasRows != 0)
            {
                return null;
            }

            return new StaticSpriteAsset(
                0,
                atlas.Width / key.AtlasColumns,
                atlas.Height / key.AtlasRows,
                0,
                0,
                atlas.Rgba8,
                key.FrameCount,
                key.FrameDurationSeconds);
        }

        if (key.SourceX + key.SourceSize > atlas.Width ||
            key.SourceY + key.SourceSize > atlas.Height)
        {
            return null;
        }

        var rgba = new byte[key.SourceSize * key.SourceSize * 4];
        for (var y = 0; y < key.SourceSize; y++)
        {
            atlas.Rgba8.AsSpan(((key.SourceY + y) * atlas.Width + key.SourceX) * 4, key.SourceSize * 4)
                .CopyTo(rgba.AsSpan(y * key.SourceSize * 4, key.SourceSize * 4));
        }

        return new StaticSpriteAsset(
            0,
            key.SourceSize,
            key.SourceSize,
            48,
            0,
            rgba);
    }

}
