using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core;
using Sacred.Assets;
using Sacred.Engine.Storage;
using Sacred.Granny;

namespace Sacred.Engine.Assets;

public sealed class AssetManager : IDisposable
{
    private const int MaxTextureCacheEntries = 64;
    private const int MaxConcurrentTextureLoads = 1;
    private static readonly PlayerCharacterDefinition[] PlayerCharacterDefinitions =
    [
        new(1, "Gladiator", "GLADIATORBACKUP.GRN", [], []),
        new(2, "Seraphim", "SERAPHIM.GRN", [], []),
        new(3, "Wood Elf", "WALDELFE.GRN", [], []),
        new(4, "Dark Elf", "DARKELVE.GRN", [], []),
        new(5, "Battle Mage", "MAGICIAN.GRN", ["MAGICIAN_COWL.GRN"], []),
        new(6, "Vampiress", "VLADY_D.GRN", [], []),
        new(7, "Dwarf", "DWARF.GRN", [], []),
        new(8, "Daemon", "DAEMONIA.GRN", [], [])
    ];

    private readonly TexturePakArchive _texturePak;
    private readonly TilesPakArchive _tilesPak;
    private readonly ItemsPakTypeArchive _itemsPak;
    private readonly MixedPakArchive _mixedPak;
    private readonly ModelsPakArchive _modelsPak;
    private readonly DirectStoragePayloadReader? _directStoragePayloadReader;
    private readonly Dictionary<string, TextureCacheEntry> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<TextureAsset>> _textureLoads = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _textureLru = [];
    private readonly Lock _textureLock = new();
    private readonly SemaphoreSlim _textureLoadThrottle = new(MaxConcurrentTextureLoads, MaxConcurrentTextureLoads);
    private readonly Dictionary<uint, StaticSpriteAsset?> _staticSprites = new();
    private readonly Dictionary<uint, Task<StaticSpriteAsset?>> _staticSpriteLoads = new();
    private readonly Lock _staticSpriteLock = new();
    private readonly Dictionary<string, GrnAsset> _grnModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<GrnAsset>> _grnModelLoads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, PlayerCharacterAsset> _playerCharacters = new();
    private readonly Dictionary<uint, Task<PlayerCharacterAsset>> _playerCharacterLoads = new();
    private readonly Lock _modelLock = new();
    private bool _disposed;

    public AssetManager(SacredGameDirectories gameDirectories)
    {
        var texturePakPath = gameDirectories.TexturesPakPath;
        var pakDirectory = Path.GetDirectoryName(texturePakPath)
            ?? throw new InvalidDataException("Cannot infer tiles.pak path from texture PAK path.");
        _directStoragePayloadReader = DirectStoragePayloadReader.TryCreate();
        _texturePak = TexturePakArchive.LoadFromDirectory(pakDirectory, _directStoragePayloadReader);
        _tilesPak = TilesPakArchive.Load(Path.Combine(pakDirectory, "tiles.pak"));
        _itemsPak = ItemsPakTypeArchive.Load(gameDirectories.ItemsPakPath);
        _mixedPak = MixedPakArchive.Load(Path.Combine(pakDirectory, "mixed.pak"));
        _modelsPak = ModelsPakArchive.Load(Path.Combine(pakDirectory, "models.pak"), _directStoragePayloadReader);
    }

    public int PlayerCharacterCount => PlayerCharacterDefinitions.Length;

    public Task<TextureAsset> LoadTextureAsync(string textureName, CancellationToken cancellationToken = default)
    {
        Task<TextureAsset> loadTask;
        lock (_textureLock)
        {
            if (_textures.TryGetValue(textureName, out var cached))
            {
                _textureLru.Remove(cached.Node);
                _textureLru.AddFirst(cached.Node);
                return Task.FromResult(cached.Asset);
            }

            if (_textureLoads.TryGetValue(textureName, out var existingLoadTask))
            {
                loadTask = existingLoadTask;
            }
            else
            {
                loadTask = Task.Run(() => LoadAndCacheTextureAsync(textureName));
                _textureLoads[textureName] = loadTask;
            }
        }

        return cancellationToken.CanBeCanceled ? loadTask.WaitAsync(cancellationToken) : loadTask;
    }

    private async Task<TextureAsset> LoadAndCacheTextureAsync(string textureName)
    {
        try
        {
            await _textureLoadThrottle.WaitAsync().ConfigureAwait(false);
            TextureAsset asset;
            try
            {
                asset = await _texturePak.LoadTextureAsync(textureName).ConfigureAwait(false);
            }
            finally
            {
                _textureLoadThrottle.Release();
            }

            lock (_textureLock)
            {
                if (_textures.TryGetValue(textureName, out var cached))
                    return cached.Asset;

                var node = new LinkedListNode<string>(textureName);
                _textureLru.AddFirst(node);
                _textures[textureName] = new TextureCacheEntry(asset, node);
                EvictOldTextures();
            }

            return asset;
        }
        finally
        {
            lock (_textureLock)
                _textureLoads.Remove(textureName);
        }
    }

    private void EvictOldTextures()
    {
        while (_textures.Count > MaxTextureCacheEntries && _textureLru.Last is { } last)
        {
            _textures.Remove(last.Value);
            _textureLru.RemoveLast();
        }
    }

    public TileDefinition? GetTileDefinition(uint tileId) => _tilesPak.Get(tileId);

    public ItemTypeRecord? GetItemType(uint typeId) => _itemsPak.Get(typeId);

    public Task<StaticSpriteAsset?> LoadStaticSpriteAsync(uint typeId, CancellationToken cancellationToken = default)
    {
        var item = _itemsPak.Get(typeId);
        if (item is null || item.Value.MixedBaseGroupId == 0)
            return Task.FromResult<StaticSpriteAsset?>(null);

        var groupId = _mixedPak.ResolveGroupId(item.Value.MixedBaseGroupId);
        if (groupId is null)
            return Task.FromResult<StaticSpriteAsset?>(null);

        Task<StaticSpriteAsset?> loadTask;
        lock (_staticSpriteLock)
        {
            if (_staticSprites.TryGetValue(groupId.Value, out var cached))
                return Task.FromResult(cached);

            if (_staticSpriteLoads.TryGetValue(groupId.Value, out var existingLoadTask))
            {
                loadTask = existingLoadTask;
            }
            else
            {
                loadTask = Task.Run(() => LoadAndCacheStaticSpriteAsync(groupId.Value));
                _staticSpriteLoads[groupId.Value] = loadTask;
            }
        }

        return cancellationToken.CanBeCanceled ? loadTask.WaitAsync(cancellationToken) : loadTask;
    }

    public bool TryGetStaticSpriteOrRequest(uint typeId, out StaticSpriteAsset? sprite)
    {
        sprite = null;

        var item = _itemsPak.Get(typeId);
        if (item is null || item.Value.MixedBaseGroupId == 0)
            return true;

        var groupId = _mixedPak.ResolveGroupId(item.Value.MixedBaseGroupId);
        if (groupId is null)
            return true;

        lock (_staticSpriteLock)
        {
            if (_staticSprites.TryGetValue(groupId.Value, out sprite))
                return true;

            if (!_staticSpriteLoads.ContainsKey(groupId.Value))
                _staticSpriteLoads[groupId.Value] = Task.Run(() => LoadAndCacheStaticSpriteAsync(groupId.Value));
        }

        return false;
    }

    private async Task<StaticSpriteAsset?> LoadAndCacheStaticSpriteAsync(uint groupId)
    {
        try
        {
            var sprite = await BuildStaticSpriteAsync(groupId).ConfigureAwait(false);
            lock (_staticSpriteLock)
                _staticSprites[groupId] = sprite;

            return sprite;
        }
        finally
        {
            lock (_staticSpriteLock)
                _staticSpriteLoads.Remove(groupId);
        }
    }

    private async Task<StaticSpriteAsset?> BuildStaticSpriteAsync(uint groupId)
    {
        var pieces = _mixedPak.GetGroup(groupId);
        if (pieces is null || pieces.Count == 0)
            return null;

        var blits = new List<StaticSpriteBlit>();
        int? minX = null;
        int? minY = null;
        int? maxX = null;
        int? maxY = null;

        foreach (var piece in pieces)
        {
            if (string.IsNullOrWhiteSpace(piece.AtlasName))
                continue;

            TextureAsset atlas;
            try
            {
                atlas = await LoadTextureAsync(piece.AtlasName).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
                continue;
            }

            var sourceLeft = Math.Clamp((int)MathF.Round(MathF.Min(piece.Uv0, piece.Uv2) * atlas.Width), 0, atlas.Width);
            var sourceTop = Math.Clamp((int)MathF.Round(MathF.Min(piece.Uv1, piece.Uv3) * atlas.Height), 0, atlas.Height);
            var sourceRight = Math.Clamp((int)MathF.Round(MathF.Max(piece.Uv0, piece.Uv2) * atlas.Width), 0, atlas.Width);
            var sourceBottom = Math.Clamp((int)MathF.Round(MathF.Max(piece.Uv1, piece.Uv3) * atlas.Height), 0, atlas.Height);
            if (sourceRight <= sourceLeft || sourceBottom <= sourceTop)
                continue;

            var destLeft = Math.Min(piece.Left, piece.Right);
            var destTop = Math.Min(piece.Top, piece.Bottom);
            var destRight = Math.Max(piece.Left, piece.Right);
            var destBottom = Math.Max(piece.Top, piece.Bottom);
            if (destRight <= destLeft || destBottom <= destTop)
                continue;

            blits.Add(new StaticSpriteBlit(
                atlas,
                sourceLeft,
                sourceTop,
                sourceRight,
                sourceBottom,
                destLeft,
                destTop,
                destRight,
                destBottom));

            minX = minX is null ? destLeft : Math.Min(minX.Value, destLeft);
            minY = minY is null ? destTop : Math.Min(minY.Value, destTop);
            maxX = maxX is null ? destRight : Math.Max(maxX.Value, destRight);
            maxY = maxY is null ? destBottom : Math.Max(maxY.Value, destBottom);
        }

        if (blits.Count == 0 || minX is null || minY is null || maxX is null || maxY is null)
            return null;

        var width = Math.Max(1, maxX.Value - minX.Value);
        var height = Math.Max(1, maxY.Value - minY.Value);
        var rgba = new byte[width * height * 4];

        foreach (var blit in blits)
        {
            BlitTextureRegion(
                blit.Atlas,
                rgba,
                width,
                height,
                blit.SourceLeft,
                blit.SourceTop,
                blit.SourceRight,
                blit.SourceBottom,
                blit.DestLeft - minX.Value,
                blit.DestTop - minY.Value,
                blit.DestRight - blit.DestLeft,
                blit.DestBottom - blit.DestTop);
        }

        return new StaticSpriteAsset(groupId, width, height, -minX.Value, -minY.Value, rgba);
    }

    private static void BlitTextureRegion(
        TextureAsset source,
        byte[] dest,
        int destWidth,
        int destHeight,
        int sourceLeft,
        int sourceTop,
        int sourceRight,
        int sourceBottom,
        int destX,
        int destY,
        int width,
        int height)
    {
        for (var y = 0; y < height; y++)
        {
            var dy = destY + y;
            if (dy < 0 || dy >= destHeight)
                continue;

            var sy = sourceTop + y * (sourceBottom - sourceTop) / height;
            for (var x = 0; x < width; x++)
            {
                var dx = destX + x;
                if (dx < 0 || dx >= destWidth)
                    continue;

                var sx = sourceLeft + x * (sourceRight - sourceLeft) / width;
                var si = (sy * source.Width + sx) * 4;
                var alpha = source.Rgba8[si + 3];
                if (alpha == 0)
                    continue;

                var di = (dy * destWidth + dx) * 4;
                if (alpha == 255 || dest[di + 3] == 0)
                {
                    dest[di + 0] = source.Rgba8[si + 0];
                    dest[di + 1] = source.Rgba8[si + 1];
                    dest[di + 2] = source.Rgba8[si + 2];
                    dest[di + 3] = alpha;
                    continue;
                }

                var destAlpha = dest[di + 3];
                var inverse = 255 - alpha;
                var outAlpha = alpha + destAlpha * inverse / 255;
                if (outAlpha == 0)
                    continue;

                var destFactor = destAlpha * inverse / 255;
                dest[di + 0] = (byte)((source.Rgba8[si + 0] * alpha + dest[di + 0] * destFactor) / outAlpha);
                dest[di + 1] = (byte)((source.Rgba8[si + 1] * alpha + dest[di + 1] * destFactor) / outAlpha);
                dest[di + 2] = (byte)((source.Rgba8[si + 2] * alpha + dest[di + 2] * destFactor) / outAlpha);
                dest[di + 3] = (byte)outAlpha;
            }
        }
    }

    public Task<GrnAsset> LoadGrnModelAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        return LoadGrnModelAsync(relativePath, GrnMeshExtractionMode.PrimarySlice, cancellationToken);
    }

    private Task<GrnAsset> LoadGrnModelAsync(
        string relativePath,
        GrnMeshExtractionMode meshExtractionMode,
        CancellationToken cancellationToken = default)
    {
        var key = Path.GetFileName(relativePath);
        var cacheKey = ModelCacheKey(key, meshExtractionMode);

        Task<GrnAsset> loadTask;
        lock (_modelLock)
        {
            if (_grnModels.TryGetValue(cacheKey, out var cached))
                return Task.FromResult(cached);

            if (_grnModelLoads.TryGetValue(cacheKey, out var existingLoadTask))
            {
                loadTask = existingLoadTask;
            }
            else
            {
                loadTask = Task.Run(() => LoadAndCacheGrnModelAsync(key, cacheKey, meshExtractionMode));
                _grnModelLoads[cacheKey] = loadTask;
            }
        }

        return cancellationToken.CanBeCanceled ? loadTask.WaitAsync(cancellationToken) : loadTask;
    }

    private async Task<GrnAsset> LoadAndCacheGrnModelAsync(
        string key,
        string cacheKey,
        GrnMeshExtractionMode meshExtractionMode)
    {
        try
        {
            var asset = await _modelsPak.LoadModelAsync(key, meshExtractionMode).ConfigureAwait(false);
            lock (_modelLock)
                _grnModels.TryAdd(cacheKey, asset);

            return asset;
        }
        finally
        {
            lock (_modelLock)
                _grnModelLoads.Remove(cacheKey);
        }
    }

    public Task<PlayerCharacterAsset> LoadPlayerCharacterAsync(uint entryId, CancellationToken cancellationToken = default)
    {
        Task<PlayerCharacterAsset> loadTask;
        lock (_modelLock)
        {
            if (_playerCharacters.TryGetValue(entryId, out var cached))
                return Task.FromResult(cached);

            if (_playerCharacterLoads.TryGetValue(entryId, out var existingLoadTask))
            {
                loadTask = existingLoadTask;
            }
            else
            {
                loadTask = Task.Run(() => LoadAndCachePlayerCharacterAsync(entryId));
                _playerCharacterLoads[entryId] = loadTask;
            }
        }

        return cancellationToken.CanBeCanceled ? loadTask.WaitAsync(cancellationToken) : loadTask;
    }

    private async Task<PlayerCharacterAsset> LoadAndCachePlayerCharacterAsync(uint entryId)
    {
        try
        {
            var definitionIndex = checked((int)entryId - 1);
            if ((uint)definitionIndex >= (uint)PlayerCharacterDefinitions.Length)
                throw new FileNotFoundException($"Player character slot {entryId} was not configured.");

            var definition = PlayerCharacterDefinitions[definitionIndex];
            var model = definition.AttachmentModelNames.Length > 0
                ? await _modelsPak.LoadCharacterModelAsync(
                    definition.ModelName,
                    definition.AttachmentModelNames,
                    definition.HiddenBaseTextureNames.Length > 0
                        ? new HashSet<string>(definition.HiddenBaseTextureNames, StringComparer.OrdinalIgnoreCase)
                        : null).ConfigureAwait(false)
                : await LoadGrnModelAsync(definition.ModelName, GrnMeshExtractionMode.PrimarySlice).ConfigureAwait(false);
            var asset = new PlayerCharacterAsset(definition.SlotId, definition.DisplayName, definition.ModelName, model);

            lock (_modelLock)
                _playerCharacters.TryAdd(entryId, asset);

            return asset;
        }
        finally
        {
            lock (_modelLock)
                _playerCharacterLoads.Remove(entryId);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_textureLock)
        {
            _textures.Clear();
            _textureLoads.Clear();
            _textureLru.Clear();
        }

        lock (_staticSpriteLock)
        {
            _staticSprites.Clear();
            _staticSpriteLoads.Clear();
        }

        lock (_modelLock)
        {
            _grnModels.Clear();
            _grnModelLoads.Clear();
            _playerCharacters.Clear();
            _playerCharacterLoads.Clear();
        }

        _texturePak.Dispose();
        _modelsPak.Dispose();
        _textureLoadThrottle.Dispose();
        _directStoragePayloadReader?.Dispose();
    }

    private sealed record TextureCacheEntry(TextureAsset Asset, LinkedListNode<string> Node);

    private static string ModelCacheKey(string modelName, GrnMeshExtractionMode meshExtractionMode) =>
        $"{meshExtractionMode}:{modelName}";

    private sealed record StaticSpriteBlit(
        TextureAsset Atlas,
        int SourceLeft,
        int SourceTop,
        int SourceRight,
        int SourceBottom,
        int DestLeft,
        int DestTop,
        int DestRight,
        int DestBottom);

    private readonly record struct PlayerCharacterDefinition(
        uint SlotId,
        string DisplayName,
        string ModelName,
        string[] AttachmentModelNames,
        string[] HiddenBaseTextureNames);
}
