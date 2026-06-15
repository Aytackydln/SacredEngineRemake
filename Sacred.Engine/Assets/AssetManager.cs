using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Mixed;
using Sacred.Assets.Paks.Models;
using Sacred.Assets.Paks.Texture;
using Sacred.Assets.Paks.Tiles;
using Sacred.Core;
using Sacred.Core.Pak.Items;
using Sacred.Granny;

namespace Sacred.Engine.Assets;

public sealed class AssetManager : IDisposable
{
    private const int MaxTextureCacheEntries = 64;
    
    private const string SeraWings = "SeraWings01.grn";
    private const string SeraHair = "SeraHair01.grn";
    private const string SeraHelm = "Seraphim_christmas_helm.GRN";

    private const string DelfClothArmor = "ELVE_MAGICAN_CLOTH.GRN";
    private const string DelfBreastplate = "DElve_sa5_body.grn";

    private const string GladBelt = "Gladiator_belt.grn";
    private const string MageCowl = "MAGICIAN_COWL.GRN";
    private const string VampDHair = "vlady_d_hair.grn";
    private const string VampNHair = "vlady_n_hair.grn";
    private const string DaemonHelm = "Daemonia_Armor01_Helm.grn";

    private static readonly string[] GladAttachments = [GladBelt];
    private static readonly string[] SeraAttachments = [SeraWings, SeraHair, SeraHelm];
    private static readonly string[] Delf1Attachments = [DelfClothArmor];
    private static readonly string[] Delf2Attachments = [DelfBreastplate];
    private static readonly string[] MageAttachments = [MageCowl];
    private static readonly string[] VampDAttachments = [VampDHair];
    private static readonly string[] VampNAttachments = [VampNHair];
    private static readonly string[] DaemonAttachments = [DaemonHelm];

    private static readonly PlayerCharacterDefinition[] PlayerCharacterDefinitions =
    [
        new(1, "Seraphim", null, SeraAttachments, []),
        new(2, "Gladiator", null, GladAttachments, []),
        new(108, "Wood Elf", "Waldelfe.grn", [], []),
        new(4, "Dark Elf 1", null, Delf1Attachments, []),
        new(4, "Dark Elf 2", null, Delf2Attachments, []),
        new(3, "Battle Mage", null, MageAttachments, []),
        new(6, "Vampiress D", "VLADY_D.GRN", VampDAttachments, []),
        new(6, "Vampiress N", "VLADY_N.GRN", VampNAttachments, []),
        new(8, "Dwarf", null, [], []),
        new(9, "Daemon", null, DaemonAttachments, [])
    ];

    private readonly TexturePakArchive _texturePak;
    private readonly TilesPakArchive _tilesPak;
    private readonly FrozenDictionary<ushort, ItemsPakEntry> _items;
    private readonly FrozenDictionary<uint, ItemsPakEntry[]> _playerCharacterItemsByItemId;
    private readonly MixedPakArchive _mixedPak;
    private readonly ModelsPakArchive _modelsPak;
    private readonly Dictionary<string, TextureCacheEntry> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<TextureAsset>> _textureLoads = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _textureLru = [];
    private readonly Lock _textureLock = new();

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
        _texturePak = TexturePakArchive.LoadFromDirectory(pakDirectory);
        _tilesPak = TilesPakArchive.Load(Path.Combine(pakDirectory, "tiles.pak"));
        var items = ItemsPakParser.Parse(gameDirectories.ItemsPakPath).ToArray();
        _items = items.ToFrozenDictionary(item => item.ItemIndex);
        _playerCharacterItemsByItemId = items
            .Where(static item => !string.IsNullOrWhiteSpace(item.ModelDesc.ModelName))
            .GroupBy(static item => item.ItemId)
            .ToFrozenDictionary(static group => group.Key, static group => group.ToArray());
        _mixedPak = MixedPakArchive.Load(Path.Combine(pakDirectory, "mixed.pak"));
        _modelsPak = ModelsPakArchive.Load(Path.Combine(pakDirectory, "models.pak"));
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
            var asset = await _texturePak.LoadTextureAsync(textureName);

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

    public ItemsPakEntry? GetItem(uint typeId)
    {
        if (typeId > ushort.MaxValue)
            return null;

        return _items.TryGetValue((ushort)typeId, out var item) ? item : null;
    }

    public Task<StaticSpriteAsset?> LoadStaticSpriteAsync(uint typeId, CancellationToken cancellationToken = default)
    {
        var item = GetItem(typeId);
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

        var item = GetItem(typeId);
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
            var sprite = await BuildStaticSpriteAsync(groupId);
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
                atlas = await LoadTextureAsync(piece.AtlasName);
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
            var asset = await _modelsPak.LoadModelAsync(key, meshExtractionMode);
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
            var item = ResolvePlayerCharacterItem(definition);
            var modelName = item.ModelDesc.ModelName;
            
            GrnAsset model;
            if (definition.AttachmentModelNames.Length > 0)
                model = await _modelsPak.LoadCharacterModelAsync(
                    modelName,
                    definition.AttachmentModelNames,
                    definition.HiddenBaseTextureNames.Length > 0
                        ? new HashSet<string>(definition.HiddenBaseTextureNames, StringComparer.OrdinalIgnoreCase)
                        : null);
            else
                model = await LoadGrnModelAsync(modelName, GrnMeshExtractionMode.PrimarySlice);

            var asset = new PlayerCharacterAsset(
                item.ItemId,
                definition.DisplayName,
                modelName,
                model);

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
    }

    private sealed record TextureCacheEntry(TextureAsset Asset, LinkedListNode<string> Node);

    private static string ModelCacheKey(string modelName, GrnMeshExtractionMode meshExtractionMode) =>
        $"{meshExtractionMode}:{modelName}";

    private ItemsPakEntry ResolvePlayerCharacterItem(PlayerCharacterDefinition definition)
    {
        if (!_playerCharacterItemsByItemId.TryGetValue(definition.ItemId, out var candidates))
            throw new FileNotFoundException($"Player character item id {definition.ItemId} was not found in Items.pak.");

        if (!string.IsNullOrWhiteSpace(definition.PreferredModelName))
        {
            foreach (var candidate in candidates)
            {
                if (candidate.ModelDesc.ModelName.Equals(definition.PreferredModelName, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
        }

        return candidates[0];
    }

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
        uint ItemId,
        string DisplayName,
        string? PreferredModelName,
        string[] AttachmentModelNames,
        string[] HiddenBaseTextureNames);
}
