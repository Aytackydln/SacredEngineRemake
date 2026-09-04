using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Graphics.Sprites;
using Sacred.Particles;

namespace Sacred.Engine.Assets;

/// <summary>Loads original Texture.pak particle atlases for world emitters.</summary>
internal sealed class WorldParticleSpriteLoader
{
    private readonly Func<string, Task<TextureAsset>> _loadTextureAsync;
    private readonly WorldSpriteLoadQueue _loadQueue;
    private readonly Dictionary<ParticleSpriteReference, StaticSpriteAsset?> _sprites = [];
    private readonly HashSet<ParticleSpriteReference> _loads = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public WorldParticleSpriteLoader(
        Func<string, Task<TextureAsset>> loadTextureAsync,
        WorldSpriteLoadQueue loadQueue)
    {
        _loadTextureAsync = loadTextureAsync;
        _loadQueue = loadQueue;
    }

    public bool TryGetOrRequest(
        ParticleSpriteReference reference,
        out StaticSpriteAsset? sprite)
    {
        sprite = null;
        if (!_lock.Wait(0))
            return false;

        try
        {
            if (_sprites.TryGetValue(reference, out sprite))
                return true;

            if (_loads.Add(reference))
                _loadQueue.Enqueue(
                    () => LoadAndCacheAsync(reference),
                    AssetLoadPriority.Background);
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

    private async Task LoadAndCacheAsync(ParticleSpriteReference reference)
    {
        StaticSpriteAsset? sprite;
        try
        {
            var atlas = await _loadTextureAsync(reference.TextureName).ConfigureAwait(false);
            sprite = BuildSprite(atlas, reference);
            if (sprite is not null)
                SpriteTransparentEdgePadding.Apply(
                    sprite.Rgba,
                    sprite.AtlasWidth,
                    sprite.AtlasHeight,
                    sprite.Width,
                    sprite.Height);
            EngineLog.WriteLine(sprite is null
                ? $"World particle atlas rejected: {reference.TextureName}."
                : $"World particle atlas loaded: {reference.TextureName} " +
                  $"({reference.AtlasColumns}x{reference.AtlasRows}, {reference.FrameCount} frames).");
        }
        catch (Exception exception)
        {
            sprite = null;
            EngineLog.WriteLine($"World particle atlas failed: {reference.TextureName}: {exception.Message}");
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _sprites[reference] = sprite;
            _loads.Remove(reference);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static StaticSpriteAsset? BuildSprite(
        TextureAsset atlas,
        ParticleSpriteReference reference)
    {
        if (reference.AtlasColumns <= 0 || reference.AtlasRows <= 0 ||
            reference.FrameCount <= 0 || reference.FrameDurationSeconds <= 0.0f ||
            reference.FrameCount > reference.AtlasColumns * reference.AtlasRows ||
            atlas.Width % reference.AtlasColumns != 0 ||
            atlas.Height % reference.AtlasRows != 0)
        {
            return null;
        }

        return new StaticSpriteAsset(
            0,
            atlas.Width / reference.AtlasColumns,
            atlas.Height / reference.AtlasRows,
            0,
            0,
            atlas.Rgba8,
            reference.FrameCount,
            reference.FrameDurationSeconds);
    }
}
