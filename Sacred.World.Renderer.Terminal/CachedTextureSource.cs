using Sacred.Assets.Paks.Texture;

namespace Sacred.World.Renderer.Terminal;

/// <summary>Export-session RAM cache. The borrowed archive remains uncached and caller-owned.</summary>
internal sealed class CachedTextureSource(TexturePakArchive archive, long capacityBytes) : ITextureSource, IDisposable
{
    private readonly DecodedTextureCache _cache = new(capacityBytes);
    public void Dispose() => _cache.Dispose();
    public (long Hits, long Misses) Statistics => (_cache.Hits, _cache.Misses);

    public Task<TextureAsset> LoadTextureAsync(string textureName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = TryResolveTextureName(textureName, out var resolved) ? resolved : textureName;
        var load = _cache.LoadAsync(key, () => archive.LoadTextureAsync(key));
        return cancellationToken.CanBeCanceled ? load.WaitAsync(cancellationToken) : load;
    }

    public Task<TextureAsset> LoadTextureAsync(uint entryId, CancellationToken cancellationToken = default) =>
        TryGetTextureName(entryId, out var name) ? LoadTextureAsync(name, cancellationToken)
            : Task.FromException<TextureAsset>(new FileNotFoundException($"Texture entry #{entryId} was not found."));

    public bool TryResolveTextureName(string name, out string resolved) => archive.TryResolveTextureName(name, out resolved);
    public bool TryGetTextureName(uint id, out string name) => archive.TryGetTextureName(id, out name);
    public bool TryResolveTextureRecord(string name, out TexturePakRecord record) =>
        archive.TryResolveTextureRecord(name, out record);
}
