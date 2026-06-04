using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Sacred.Core;
using Sacred.Core.Assets;

namespace Sacred.Engine.Assets;

public sealed class AssetManager : IDisposable
{
    private const int MaxTextureCacheEntries = 64;
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
    private readonly Dictionary<string, TextureCacheEntry> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _textureLru = [];
    private readonly Lock _textureLock = new();
    private readonly Dictionary<uint, StaticSpriteAsset?> _staticSprites = new();
    private readonly Lock _staticSpriteLock = new();
    private readonly Dictionary<string, GrnAsset> _grnModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, PlayerCharacterAsset> _playerCharacters = new();

    public AssetManager(SacredGameDirectories gameDirectories)
    {
        var texturePakPath = gameDirectories.TexturesPakPath;
        _texturePak = TexturePakArchive.Load(texturePakPath);
        var pakDirectory = Path.GetDirectoryName(texturePakPath)
            ?? throw new InvalidDataException("Cannot infer tiles.pak path from texture PAK path.");
        _tilesPak = TilesPakArchive.Load(Path.Combine(pakDirectory, "tiles.pak"));
        _itemsPak = ItemsPakTypeArchive.Load(gameDirectories.ItemsPakPath);
        _mixedPak = MixedPakArchive.Load(Path.Combine(pakDirectory, "mixed.pak"));
        _modelsPak = ModelsPakArchive.Load(Path.Combine(pakDirectory, "models.pak"));
    }

    public int PlayerCharacterCount => PlayerCharacterDefinitions.Length;

    public TextureAsset LoadTexture(string textureName)
    {
        lock (_textureLock)
        {
            if (_textures.TryGetValue(textureName, out var cached))
            {
                _textureLru.Remove(cached.Node);
                _textureLru.AddFirst(cached.Node);
                return cached.Asset;
            }

            var asset = _texturePak.LoadTexture(textureName);
            var node = new LinkedListNode<string>(textureName);
            _textureLru.AddFirst(node);
            _textures[textureName] = new TextureCacheEntry(asset, node);
            EvictOldTextures();
            return asset;
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

    public StaticSpriteAsset? LoadStaticSprite(uint typeId)
    {
        var item = _itemsPak.Get(typeId);
        if (item is null || item.Value.MixedBaseGroupId == 0)
            return null;

        var groupId = _mixedPak.ResolveGroupId(item.Value.MixedBaseGroupId);
        if (groupId is null)
            return null;

        lock (_staticSpriteLock)
        {
            if (_staticSprites.TryGetValue(groupId.Value, out var cached))
                return cached;

            var sprite = BuildStaticSprite(groupId.Value);
            _staticSprites[groupId.Value] = sprite;
            return sprite;
        }
    }

    private StaticSpriteAsset? BuildStaticSprite(uint groupId)
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
                atlas = LoadTexture(piece.AtlasName);
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

    public GrnAsset LoadGrnModel(string relativePath)
    {
        return LoadGrnModel(relativePath, GrnMeshExtractionMode.PrimarySlice);
    }

    private GrnAsset LoadGrnModel(string relativePath, GrnMeshExtractionMode meshExtractionMode)
    {
        var key = Path.GetFileName(relativePath);
        var cacheKey = ModelCacheKey(key, meshExtractionMode);
        if (_grnModels.TryGetValue(cacheKey, out var cached)) return cached;

        var asset = _modelsPak.LoadModel(key, meshExtractionMode);

        _grnModels.Add(cacheKey, asset);
        return asset;
    }

    public PlayerCharacterAsset LoadPlayerCharacter(uint entryId)
    {
        if (_playerCharacters.TryGetValue(entryId, out var cached))
            return cached;

        var definitionIndex = checked((int)entryId - 1);
        if ((uint)definitionIndex >= (uint)PlayerCharacterDefinitions.Length)
            throw new FileNotFoundException($"Player character slot {entryId} was not configured.");

        var definition = PlayerCharacterDefinitions[definitionIndex];
        var model = definition.AttachmentModelNames.Length > 0
            ? _modelsPak.LoadCharacterModel(
                definition.ModelName,
                definition.AttachmentModelNames,
                definition.HiddenBaseTextureNames.Length > 0
                    ? new HashSet<string>(definition.HiddenBaseTextureNames, StringComparer.OrdinalIgnoreCase)
                    : null)
            : LoadGrnModel(definition.ModelName, GrnMeshExtractionMode.PrimarySlice);
        var asset = new PlayerCharacterAsset(definition.SlotId, definition.DisplayName, definition.ModelName, model);
        _playerCharacters.Add(entryId, asset);
        return asset;
    }

    public void Dispose()
    {
        lock (_textureLock)
        {
            _textures.Clear();
            _textureLru.Clear();
        }

        lock (_staticSpriteLock)
            _staticSprites.Clear();

        _grnModels.Clear();
        _playerCharacters.Clear();
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
