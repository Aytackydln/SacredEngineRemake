using System;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Graphics.Sprites;

namespace Sacred.Engine.Assets;

/// <summary>Loads Sacred's atlas of authored static-world shadow masks.</summary>
internal sealed class StaticShadowAtlasLoader
{
    private const string TextureName = "SHADOW_TREE00.TGA";
    public const int Columns = 16;
    public const int Rows = 16;

    private readonly Func<string, Task<TextureAsset>> _loadTextureAsync;
    private readonly WorldSpriteLoadQueue _loadQueue;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private StaticSpriteAsset? _atlas;
    private bool _loadRequested;
    private bool _loadComplete;

    public StaticShadowAtlasLoader(
        Func<string, Task<TextureAsset>> loadTextureAsync,
        WorldSpriteLoadQueue loadQueue)
    {
        _loadTextureAsync = loadTextureAsync;
        _loadQueue = loadQueue;
    }

    public bool TryGetOrRequest(out StaticSpriteAsset? atlas)
    {
        atlas = null;
        if (!_lock.Wait(0))
            return false;

        try
        {
            if (_loadComplete)
            {
                atlas = _atlas;
                return true;
            }

            if (!_loadRequested)
            {
                _loadRequested = true;
                _loadQueue.Enqueue(LoadAsync, AssetLoadPriority.Background);
            }

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
            _atlas = null;
            _loadRequested = false;
            _loadComplete = false;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task LoadAsync()
    {
        StaticSpriteAsset? atlas = null;
        try
        {
            var texture = await _loadTextureAsync(TextureName).ConfigureAwait(false);
            if (texture.Width % Columns == 0 && texture.Height % Rows == 0)
            {
                atlas = new StaticSpriteAsset(
                    0,
                    texture.Width,
                    texture.Height,
                    0,
                    0,
                    texture.Rgba8);
                SpriteTransparentEdgePadding.Apply(
                    atlas.Rgba,
                    atlas.AtlasWidth,
                    atlas.AtlasHeight,
                    atlas.Width,
                    atlas.Height);
            }

            EngineLog.WriteLine(atlas is null
                ? $"Static shadow atlas rejected: {TextureName}."
                : $"Static shadow atlas loaded: {TextureName} ({texture.Width}x{texture.Height}).");
        }
        catch (Exception exception)
        {
            EngineLog.WriteLine($"Static shadow atlas failed: {TextureName}: {exception.Message}");
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _atlas = atlas;
            _loadComplete = true;
            _loadRequested = false;
        }
        finally
        {
            _lock.Release();
        }
    }
}
