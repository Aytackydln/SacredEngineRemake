using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;

namespace Sacred.Engine.Assets;

/// <summary>
/// Loads the atlas-backed <c>MiniObjTex*</c> world decals. These use a static
/// record's three sprite parameters instead of a mixed.pak sprite group.
/// </summary>
internal sealed class MiniObjectSpriteLoader
{
    private const int AtlasSize = 256;
    private const int SpriteAnchorX = 48;

    private readonly Func<string, Task<TextureAsset>> _loadTextureAsync;
    private readonly WorldSpriteLoadQueue _loadQueue;
    private readonly Dictionary<MiniObjectSpriteKey, StaticSpriteAsset?> _sprites = [];
    private readonly HashSet<MiniObjectSpriteKey> _loads = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public MiniObjectSpriteLoader(
        Func<string, Task<TextureAsset>> loadTextureAsync,
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
        out StaticSpriteAsset? sprite)
    {
        sprite = null;
        if (!TryCreateKey(item.ModelDesc.ModelName, sourceX, sourceY, sourceSize, out var key))
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

    private async Task<StaticSpriteAsset?> LoadAndCacheAsync(MiniObjectSpriteKey key)
    {
        StaticSpriteAsset? sprite;
        try
        {
            var atlas = await _loadTextureAsync(key.TextureName).ConfigureAwait(false);
            sprite = BuildSprite(atlas, key);
        }
        catch (Exception)
        {
            sprite = null;
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

    private static StaticSpriteAsset? BuildSprite(TextureAsset atlas, MiniObjectSpriteKey key)
    {
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

        return new StaticSpriteAsset(0, key.SourceSize, key.SourceSize, SpriteAnchorX, 0, rgba);
    }

    private static bool TryCreateKey(
        string modelName,
        byte sourceX,
        byte sourceY,
        byte sourceSize,
        out MiniObjectSpriteKey key)
    {
        key = default;
        const string prefix = "MiniObjTex";
        if (!modelName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(modelName.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var atlasIndex) ||
            sourceSize == 0 ||
            AtlasSize % sourceSize != 0)
        {
            return false;
        }

        key = new MiniObjectSpriteKey(
            $"MINIOBJ{AtlasSize / sourceSize}_{atlasIndex}.TGA",
            sourceX,
            sourceY,
            sourceSize);
        return true;
    }

    private readonly record struct MiniObjectSpriteKey(string TextureName, int SourceX, int SourceY, int SourceSize);
}
