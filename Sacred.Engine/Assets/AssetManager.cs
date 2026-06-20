using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Mixed;
using Sacred.Assets.Paks.Models;
using Sacred.Assets.Paks.Texture;
using Sacred.Assets.Paks.Tiles;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Engine.Rendering.EquipmentEffects;
using Sacred.Granny;

namespace Sacred.Engine.Assets;

public sealed class AssetManager : IDisposable
{
    // dictionaries should have fixed size to prevent resizing during game thus crash
    private const int MaxTextureCacheEntries = 64;
    private const int MaxModelTextureCacheEntries = 128;
    private const int DefaultMaxCache = 256;
    
    // These are game item ids (the ItemId field in Items.pak), not descriptor row indexes.
    private const uint Seraphim = 1; // SERAPHIM.GRN
    private const uint Gladiator = 2; // GLADIATOR.GRN
    private const uint BattleMage = 3; // MAGICIAN.GRN
    private const uint DarkElf = 4; // DARKELVE.GRN
    private const uint Vampiress = 6; // VLADY_D.GRN and VLADY_N.GRN
    private const uint Dwarf = 8; // dwarf.grn
    private const uint Daemon = 9; // Daemonia.grn
    private const uint WoodElf = 108; // Waldelfe.grn

    private const string ShieldAttachBone = "Bip01 L Forearm";
    private const string LeftWeaponAttachBone = "Bip01 L Hand";
    private const string RightWeaponAttachBone = "Bip01 R Hand";
    private const string LeftWeaponAnchorBone = "Bone_weapon_01";
    private const string RightWeaponAnchorBone = "Bone_weapon_02";

    private const uint DaemonHelm = 1222; // Daemonia_Armor01_Helm.grn
    private const uint DarkElfBreastplate = 1251; // DElve_sa5_body.grn
    private const uint SeraphimHelm = 1840; // Seraphim_christmas_helm.GRN
    private const uint SeraphimGodsShield = 4017;
    private const uint DarkElfClothArmor = 3160; // ELVE_MAGICAN_CLOTH.GRN
    private const uint BattleMageCowl = 3219; // magician_cowl.grn
    private const uint SeraphimWings = 4006; // SeraWings01.grn, animated wing effect row
    private const uint SeraphimHair = 4007; // SeraHair01.grn
    private const uint SeraphimArms = 4082;
    private const uint SeraphimBoots = 4083;
    private const uint SeraphimShoulder = 4084;
    private const uint VampiressDayHair = 4028; // vlady_d_hair.grn
    private const uint VampiressNightHair = 4029; // vlady_n_hair.grn
    private const uint GladiatorBelt = 4054; // Gladiator_belt.grn

    private const uint SeraphimSword = 1851;
    private const uint ElvenBow = 1747;
    private const uint VampireSword = 1771;
    private const uint BattleMageStaff = 1877;

    private const uint LargeTorch = 5633;

    private static readonly PlayerCharacterAttachment[] SeraphimAttachments = [
        new(SeraphimSword, RightWeaponAttachBone, LeftWeaponAnchorBone),
        new(SeraphimWings),
        new(SeraphimHair),
        new(SeraphimHelm),
        new(SeraphimGodsShield, ShieldAttachBone, RightWeaponAnchorBone),
        new(SeraphimArms),
        new(SeraphimBoots),
        new(SeraphimShoulder)
    ];

    private static readonly PlayerCharacterAttachment[] DalmarSet = [
        new(3271),
        new(3272),
        new(3273),
        new(3274),
        new(VampireSword, RightWeaponAttachBone, LeftWeaponAnchorBone),
    ];

    private static readonly IReadOnlyDictionary<string, ModelTextureReference> EmptyTextureAliases =
        new Dictionary<string, ModelTextureReference>(StringComparer.OrdinalIgnoreCase);

    private static readonly PlayerCharacterDefinition[] PlayerCharacterDefinitions =
    [
        new(Seraphim, "SERAPHIM.GRN", "Seraphim", SeraphimAttachments),
        new(Gladiator, "GLADIATOR.GRN", "Gladiator", [new(GladiatorBelt)]),
        new(WoodElf, "Waldelfe.grn", "Wood Elf", [new(ElvenBow, RightWeaponAttachBone, RightWeaponAnchorBone)]),
        new(DarkElf, "DARKELVE.GRN", "Dark Elf 1", [new(DarkElfClothArmor)]),
        new(DarkElf, "DARKELVE.GRN", "Dark Elf 2", [new(DarkElfBreastplate)]),
        new(BattleMage, "MAGICIAN.GRN", "Battle Mage", [new(BattleMageCowl), new(BattleMageStaff, LeftWeaponAttachBone, RightWeaponAnchorBone)]),
        new(Vampiress, "VLADY_D.GRN", "Vampiress D", DalmarSet),
        new(Vampiress, "VLADY_D.GRN", "Vampiress D (Dalmar)", [new(VampiressDayHair), new (LargeTorch, RightWeaponAttachBone, LeftWeaponAnchorBone)]),
        new(Vampiress, "VLADY_N.GRN", "Vampiress N", [new(VampiressNightHair)]),
        new(Dwarf, "dwarf.grn", "Dwarf", []),
        new(Daemon, "Daemonia.grn", "Daemon", [new(DaemonHelm)])
    ];

    private readonly TexturePakArchive _texturePak;
    private readonly TexturePakArchive _modelTexturePak;
    private readonly TilesPakArchive _tilesPak;
    private readonly FrozenDictionary<ushort, ItemsPakEntry> _itemsByModelId;
    private readonly FrozenDictionary<uint, ItemsPakEntry[]> _itemsByItemId;
    private readonly FrozenDictionary<ushort, SacredEquipment> _equipmentByModelId;
    private readonly MixedPakArchive _mixedPak;
    private readonly ModelsPakArchive _modelsPak;
    private readonly Dictionary<string, TextureCacheEntry> _textures = new(MaxTextureCacheEntries, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<TextureAsset>> _textureLoads = new(MaxTextureCacheEntries, StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _textureLru = [];
    private readonly SemaphoreSlim _textureLock = new(1, 1);

    private readonly Dictionary<string, TextureCacheEntry> _modelTextures = new(MaxModelTextureCacheEntries, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<TextureAsset>> _modelTextureLoads = new(MaxModelTextureCacheEntries, StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _modelTextureLru = [];
    private readonly SemaphoreSlim _modelTextureLock = new(1, 1);

    private readonly Dictionary<StaticSpriteAssetKey, StaticSpriteAsset?> _staticSprites = new(DefaultMaxCache);
    private readonly Dictionary<StaticSpriteAssetKey, Task<StaticSpriteAsset?>> _staticSpriteLoads = new(DefaultMaxCache);
    private readonly SemaphoreSlim _staticSpriteLock = new(1, 1);
    private readonly MiniObjectSpriteLoader _miniObjectSprites;

    private readonly Dictionary<string, TextureFrameSequenceAsset?> _textureFrameSequences =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<TextureFrameSequenceAsset?>> _textureFrameSequenceLoads =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _textureFrameSequenceLock = new(1, 1);

    private readonly Dictionary<string, GrnAsset> _grnModels = new(DefaultMaxCache, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<GrnAsset>> _grnModelLoads = new(DefaultMaxCache, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, PlayerCharacterAsset> _playerCharacters = new(DefaultMaxCache);
    private readonly SemaphoreSlim _modelLock = new(1, 1);

    private readonly Dictionary<string, GrnAnimationClip?> _playerCharacterAnimations =
        new(DefaultMaxCache, StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _playerAnimationLock = new(1, 1);
    private bool _disposed;

    public AssetManager(SacredGameDirectories gameDirectories)
    {
        var texturePakPath = gameDirectories.TexturesPakPath;
        var pakDirectory = Path.GetDirectoryName(texturePakPath)
            ?? throw new InvalidDataException("Cannot infer tiles.pak path from texture PAK path.");
        _texturePak = TexturePakArchive.LoadFromDirectory(pakDirectory);
        // World streaming can enqueue thousands of texture reads. Keep latency-sensitive model
        // materials on independent PAK streams so they are never stuck behind that queue.
        _modelTexturePak = TexturePakArchive.LoadFromDirectory(pakDirectory);
        _tilesPak = TilesPakArchive.Load(Path.Combine(pakDirectory, "tiles.pak"));
        var items = ItemsPakArchive.Load(gameDirectories.ItemsPakPath).ToArray();
        _itemsByModelId = items.ToFrozenDictionary(static item => item.ItemIndex);
        _itemsByItemId = items
            .GroupBy(static item => item.ItemId)
            .ToFrozenDictionary(static group => group.Key, static group => group.ToArray());
        _equipmentByModelId = WeaponPakParser.Parse(gameDirectories.WeaponsPakPath, _itemsByModelId)
            .ToFrozenDictionary(static equipment => checked((ushort)equipment.IdemId));
        _mixedPak = MixedPakArchive.Load(Path.Combine(pakDirectory, "mixed.pak"));
        _miniObjectSprites = new MiniObjectSpriteLoader(textureName => LoadTextureAsync(textureName));
        _modelsPak = ModelsPakArchive.Load(
            Path.Combine(pakDirectory, "models.pak"),
            Path.Combine(pakDirectory, "Models.tmp"));
    }

    public int PlayerCharacterCount => PlayerCharacterDefinitions.Length;

    public Task<TextureAsset> LoadTextureAsync(string textureName, CancellationToken cancellationToken = default)
    {
        return LoadTextureAsync(
            _texturePak,
            textureName,
            _textures,
            _textureLoads,
            _textureLru,
            _textureLock,
            MaxTextureCacheEntries,
            runOnWorker: true,
            cancellationToken);
    }

    public Task<TextureAsset> LoadModelTextureAsync(string textureName, CancellationToken cancellationToken = default)
    {
        return LoadTextureAsync(
            _modelTexturePak,
            textureName,
            _modelTextures,
            _modelTextureLoads,
            _modelTextureLru,
            _modelTextureLock,
            MaxModelTextureCacheEntries,
            runOnWorker: true,
            cancellationToken);
    }

    private async Task<TextureAsset> LoadTextureAsync(
        TexturePakArchive archive,
        string textureName,
        IDictionary<string, TextureCacheEntry> cache,
        Dictionary<string, Task<TextureAsset>> loads,
        LinkedList<string> lru,
        SemaphoreSlim cacheLock,
        int maxCacheEntries,
        bool runOnWorker,
        CancellationToken cancellationToken)
    {
        Task<TextureAsset> loadTask;
        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cache.TryGetValue(textureName, out var cached))
            {
                lru.Remove(cached.Node);
                lru.AddFirst(cached.Node);
                return cached.Asset;
            }

            if (loads.TryGetValue(textureName, out var existingLoadTask))
            {
                loadTask = existingLoadTask;
            }
            else
            {
                loadTask = runOnWorker
                    ? Task.Run(() => LoadAndCacheTextureAsync(
                        archive,
                        textureName,
                        cache,
                        loads,
                        lru,
                        cacheLock,
                        maxCacheEntries), CancellationToken.None)
                    : LoadAndCacheTextureAsync(
                        archive,
                        textureName,
                        cache,
                        loads,
                        lru,
                        cacheLock,
                        maxCacheEntries);
                loads[textureName] = loadTask;
            }
        }
        finally
        {
            cacheLock.Release();
        }

        return await (cancellationToken.CanBeCanceled ? loadTask.WaitAsync(cancellationToken) : loadTask)
            .ConfigureAwait(false);
    }

    private async Task<TextureAsset> LoadAndCacheTextureAsync(
        TexturePakArchive archive,
        string textureName,
        IDictionary<string, TextureCacheEntry> cache,
        Dictionary<string, Task<TextureAsset>> loads,
        LinkedList<string> lru,
        SemaphoreSlim cacheLock,
        int maxCacheEntries)
    {
        TextureAsset asset;
        try
        {
            asset = await archive.LoadTextureAsync(textureName).ConfigureAwait(false);
        }
        catch
        {
            await cacheLock.WaitAsync().ConfigureAwait(false);
            try
            {
                loads.Remove(textureName);
            }
            finally
            {
                cacheLock.Release();
            }

            throw;
        }

        await cacheLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (cache.TryGetValue(textureName, out var cached))
                return cached.Asset;

            var node = new LinkedListNode<string>(textureName);
            lru.AddFirst(node);
            cache[textureName] = new TextureCacheEntry(asset, node);
            EvictOldTextures(cache, lru, maxCacheEntries);

            return asset;
        }
        finally
        {
            loads.Remove(textureName);
            cacheLock.Release();
        }
    }

    private static void EvictOldTextures(
        IDictionary<string, TextureCacheEntry> cache,
        LinkedList<string> lru,
        int maxCacheEntries)
    {
        while (cache.Count > maxCacheEntries && lru.Last is { } last)
        {
            cache.Remove(last.Value);
            lru.RemoveLast();
        }
    }

    public TileDefinition? GetTileDefinition(uint tileId) => _tilesPak.Get(tileId);

    public ItemsPakEntry? GetItem(uint typeId)
    {
        if (typeId > ushort.MaxValue)
            return null;

        return _itemsByModelId.TryGetValue((ushort)typeId, out var item) ? item : null;
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

        var frameCount = Math.Max(1, (int)item.Value.StaticSpriteFrameCount);
        var frameDuration10Ms = frameCount > 1 ? item.Value.StaticSpriteFrameDuration10Ms : (byte)0;
        var key = new StaticSpriteAssetKey(groupId.Value, frameCount, frameDuration10Ms);

        // Render polling must never queue behind a loader publishing its result.
        if (!_staticSpriteLock.Wait(0))
            return false;

        try
        {
            if (_staticSprites.TryGetValue(key, out sprite))
                return true;

            if (!_staticSpriteLoads.ContainsKey(key))
                _staticSpriteLoads[key] = Task.Run(() => LoadAndCacheStaticSpriteAsync(key));

            return false;
        }
        finally
        {
            _staticSpriteLock.Release();
        }
    }

    public bool TryGetMiniObjectSpriteOrRequest(
        uint typeId,
        byte sourceX,
        byte sourceY,
        byte sourceSize,
        out StaticSpriteAsset? sprite)
    {
        sprite = null;
        var item = GetItem(typeId);
        return item is null || _miniObjectSprites.TryGetOrRequest(item.Value, sourceX, sourceY, sourceSize, out sprite);
    }

    public bool TryGetTextureFrameSequenceOrRequest(
        string frameNameFormat,
        int frameCount,
        out TextureFrameSequenceAsset? sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frameNameFormat);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);

        var key = TextureFrameSequenceCacheKey(frameNameFormat, frameCount);
        sequence = null;
        // A missed non-blocking poll is retried by the terrain snapshot builder next frame.
        if (!_textureFrameSequenceLock.Wait(0))
            return false;

        try
        {
            if (_textureFrameSequences.TryGetValue(key, out sequence))
                return true;

            if (!_textureFrameSequenceLoads.ContainsKey(key))
            {
                _textureFrameSequenceLoads[key] = Task.Run(
                    () => LoadAndCacheTextureFrameSequenceAsync(key, frameNameFormat, frameCount));
            }

            return false;
        }
        finally
        {
            _textureFrameSequenceLock.Release();
        }
    }

    private async Task<TextureFrameSequenceAsset?> LoadAndCacheTextureFrameSequenceAsync(
        string key,
        string frameNameFormat,
        int frameCount)
    {
        try
        {
            var sequence = await BuildTextureFrameSequenceAsync(frameNameFormat, frameCount).ConfigureAwait(false);
            await _textureFrameSequenceLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _textureFrameSequences[key] = sequence;
            }
            finally
            {
                _textureFrameSequenceLoads.Remove(key);
                _textureFrameSequenceLock.Release();
            }

            return sequence;
        }
        catch
        {
            await _textureFrameSequenceLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _textureFrameSequenceLoads.Remove(key);
                _textureFrameSequences[key] = null;
            }
            finally
            {
                _textureFrameSequenceLock.Release();
            }

            return null;
        }
    }

    private async Task<TextureFrameSequenceAsset> BuildTextureFrameSequenceAsync(
        string frameNameFormat,
        int frameCount)
    {
        TextureAsset? firstFrame = null;
        byte[]? atlas = null;
        var frameByteCount = 0;
        var atlasWidth = 0;
        var atlasColumns = 1;

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frameName = string.Format(CultureInfo.InvariantCulture, frameNameFormat, frameIndex);
            var frame = await LoadTextureAsync(frameName).ConfigureAwait(false);
            if (firstFrame is null)
            {
                firstFrame = frame;
                frameByteCount = checked(frame.Width * frame.Height * 4);
                atlasColumns = TextureFrameAtlasLayout.CalculateColumns(frame.Width, frame.Height, frameCount);
                var atlasRows = TextureFrameAtlasLayout.CalculateRows(frameCount, atlasColumns);
                atlasWidth = checked(frame.Width * atlasColumns);
                atlas = new byte[checked(atlasWidth * frame.Height * atlasRows * 4)];
            }
            else if (frame.Width != firstFrame.Width || frame.Height != firstFrame.Height)
            {
                throw new InvalidDataException(
                    $"Texture frame '{frame.Name}' is {frame.Width}x{frame.Height}; " +
                    $"sequence '{frameNameFormat}' starts at {firstFrame.Width}x{firstFrame.Height}.");
            }

            if (frame.Rgba8.Length != frameByteCount)
                throw new InvalidDataException($"Texture frame '{frame.Name}' has an invalid decoded byte count.");

            var frameColumn = frameIndex % atlasColumns;
            var frameRow = frameIndex / atlasColumns;
            for (var y = 0; y < frame.Height; y++)
            {
                frame.Rgba8.AsSpan(y * frame.Width * 4, frame.Width * 4).CopyTo(
                    atlas!.AsSpan(
                        ((frameRow * frame.Height + y) * atlasWidth + frameColumn * frame.Width) * 4,
                        frame.Width * 4));
            }
        }

        return new TextureFrameSequenceAsset(
            TextureFrameSequenceCacheKey(frameNameFormat, frameCount),
            firstFrame!.Width,
            firstFrame.Height,
            frameCount,
            atlas!);
    }

    private static string TextureFrameSequenceCacheKey(string frameNameFormat, int frameCount) =>
        $"{frameCount}:{frameNameFormat}";

    private async Task<StaticSpriteAsset?> LoadAndCacheStaticSpriteAsync(StaticSpriteAssetKey key)
    {
        try
        {
            var sprite = await BuildStaticSpriteAsync(key);
            await _staticSpriteLock.WaitAsync();
            try
            {
                _staticSprites[key] = sprite;
            }
            finally
            {
                _staticSpriteLoads.Remove(key);
                _staticSpriteLock.Release();
            }

            return sprite;
        }
        catch
        {
            await _staticSpriteLock.WaitAsync();
            try
            {
                _staticSpriteLoads.Remove(key);
                _staticSprites[key] = null;
            }
            finally
            {
                _staticSpriteLock.Release();
            }

            return null;
        }
    }

    private async Task<StaticSpriteAsset?> BuildStaticSpriteAsync(StaticSpriteAssetKey key)
    {
        var frames = new StaticSpriteAsset[key.FrameCount];
        for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            var groupId = checked(key.GroupId + (uint)frameIndex);
            var frame = await BuildStaticSpriteFrameAsync(groupId);
            if (frame is null)
                return null;

            frames[frameIndex] = frame;
        }

        if (frames.Length == 1)
            return frames[0];

        var minX = frames.Min(static frame => -frame.AnchorX);
        var minY = frames.Min(static frame => -frame.AnchorY);
        var maxX = frames.Max(static frame => -frame.AnchorX + frame.Width);
        var maxY = frames.Max(static frame => -frame.AnchorY + frame.Height);
        var width = Math.Max(1, maxX - minX);
        var height = Math.Max(1, maxY - minY);
        var atlasColumns = TextureFrameAtlasLayout.CalculateColumns(width, height, frames.Length);
        var atlasRows = TextureFrameAtlasLayout.CalculateRows(frames.Length, atlasColumns);
        var atlasWidth = checked(width * atlasColumns);
        var atlasHeight = checked(height * atlasRows);
        var rgba = new byte[checked(atlasWidth * atlasHeight * 4)];

        for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
        {
            var frame = frames[frameIndex];
            var destX = frameIndex % atlasColumns * width - frame.AnchorX - minX;
            var destY = frameIndex / atlasColumns * height - frame.AnchorY - minY;
            for (var y = 0; y < frame.Height; y++)
            {
                Buffer.BlockCopy(
                    frame.Rgba,
                    y * frame.Width * 4,
                    rgba,
                    ((destY + y) * atlasWidth + destX) * 4,
                    frame.Width * 4);
            }
        }

        return new StaticSpriteAsset(
            key.GroupId,
            width,
            height,
            -minX,
            -minY,
            rgba,
            frames.Length,
            key.FrameDuration10Ms * 0.01f);
    }

    private async Task<StaticSpriteAsset?> BuildStaticSpriteFrameAsync(uint groupId)
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

    private async Task<GrnAsset> LoadGrnModelAsync(
        string relativePath,
        GrnMeshExtractionMode meshExtractionMode,
        CancellationToken cancellationToken = default)
    {
        var key = Path.GetFileName(relativePath);
        var cacheKey = ModelCacheKey(key, meshExtractionMode);

        Task<GrnAsset> loadTask;
        await _modelLock.WaitAsync(cancellationToken);
        try
        {
            if (_grnModels.TryGetValue(cacheKey, out var cached))
                return cached;

            if (_grnModelLoads.TryGetValue(cacheKey, out var existingLoadTask))
            {
                loadTask = existingLoadTask;
            }
            else
            {
                loadTask = Task.Run(
                    () => LoadAndCacheGrnModelAsync(key, cacheKey, meshExtractionMode),
                    CancellationToken.None);
                _grnModelLoads[cacheKey] = loadTask;
            }
        }
        finally
        {
            _modelLock.Release();
        }

        return await (cancellationToken.CanBeCanceled ? loadTask.WaitAsync(cancellationToken) : loadTask);
    }

    private async Task<GrnAsset> LoadAndCacheGrnModelAsync(
        string key,
        string cacheKey,
        GrnMeshExtractionMode meshExtractionMode)
    {
        GrnAsset asset;
        try
        {
            asset = await _modelsPak.LoadModelAsync(key, meshExtractionMode);
        }
        catch
        {
            await _modelLock.WaitAsync();
            try
            {
                _grnModelLoads.Remove(cacheKey);
            }
            finally
            {
                _modelLock.Release();
            }

            throw;
        }

        await _modelLock.WaitAsync();
        try
        {
            if (_grnModels.TryGetValue(cacheKey, out var cached))
                return cached;

            _grnModels.Add(cacheKey, asset);
            return asset;
        }
        finally
        {
            _grnModelLoads.Remove(cacheKey);
            _modelLock.Release();
        }
    }

    private async Task<GrnAsset> LoadPlayerAttachmentModelAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        var key = Path.GetFileName(relativePath);
        var cacheKey = ModelCacheKey(key, GrnMeshExtractionMode.PrimarySlice);

        await _modelLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_grnModels.TryGetValue(cacheKey, out var cached))
                return cached;
        }
        finally
        {
            _modelLock.Release();
        }

        var asset = await _modelsPak
            .LoadModelAsync(key, GrnMeshExtractionMode.PrimarySlice, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        await _modelLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_grnModels.TryGetValue(cacheKey, out var cached))
                return cached;

            _grnModels.Add(cacheKey, asset);
            return asset;
        }
        finally
        {
            _modelLock.Release();
        }
    }

    public async Task<PlayerCharacterAsset> LoadPlayerCharacterAsync(uint entryId, CancellationToken cancellationToken = default)
    {
        await _modelLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_playerCharacters.TryGetValue(entryId, out var cached))
                return cached;
        }
        finally
        {
            _modelLock.Release();
        }

        var asset = await Task.Run(
                () => LoadPlayerCharacterCoreAsync(entryId, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        await _modelLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_playerCharacters.TryGetValue(entryId, out var cached))
                return cached;

            _playerCharacters.Add(entryId, asset);
            return asset;
        }
        finally
        {
            _modelLock.Release();
        }
    }

    private async Task<PlayerCharacterAsset> LoadPlayerCharacterCoreAsync(
        uint entryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definition = GetPlayerCharacterDefinition(entryId);
        var item = ResolvePlayerCharacterItem(definition);
        var attachmentItems = ResolvePlayerCharacterItems(definition.Attachments);
        var modelName = item.ModelDesc.ModelName;

        var model = await _modelsPak.LoadCharacterBaseModelAsync(
                modelName,
                attachmentItems
                    .Select(static attachment => new ModelAttachmentReference(
                        attachment.Item.ModelDesc.ModelName,
                        attachment.Attachment.RigidAttachBoneName,
                        attachment.Attachment.SourceAttachBoneName))
                    .ToArray(),
                cancellationToken)
            .ConfigureAwait(false);

        var attachmentModels = await Task.WhenAll(attachmentItems.Select(attachmentItem =>
            LoadPlayerAttachmentModelAsync(
                attachmentItem.Item.ModelDesc.ModelName,
                cancellationToken))).ConfigureAwait(false);
        var textureAliases = CreatePlayerCharacterTextureAliases(
                model,
                item,
                attachmentItems,
                attachmentModels);
        var equipmentEffects = EquipmentEffectSceneFactory.Create(
            model,
            CreateEquipmentEffectAttachments(attachmentItems, attachmentModels));
        cancellationToken.ThrowIfCancellationRequested();

        return new PlayerCharacterAsset(
            item.ItemIndex,
            definition.DisplayName,
            modelName,
            model,
            textureAliases,
            equipmentEffects);
    }

    public async Task<GrnAnimationClip?> LoadPlayerCharacterAnimationAsync(
        uint entryId,
        CancellationToken cancellationToken = default)
    {
        var modelName = GetPlayerCharacterDefinition(entryId).ModelName;
        await _playerAnimationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_playerCharacterAnimations.TryGetValue(modelName, out var cached))
                return cached;
        }
        finally
        {
            _playerAnimationLock.Release();
        }

        var animation = await Task.Run(
                () => _modelsPak.LoadDefaultCharacterAnimationAsync(modelName, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        await _playerAnimationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_playerCharacterAnimations.TryGetValue(modelName, out var cached))
                return cached;

            _playerCharacterAnimations.Add(modelName, animation);
        }
        finally
        {
            _playerAnimationLock.Release();
        }

        return animation;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _textureLock.Wait();
        _textures.Clear();
        _textureLoads.Clear();
        _textureLru.Clear();

        _modelTextureLock.Wait();
        _modelTextures.Clear();
        _modelTextureLoads.Clear();
        _modelTextureLru.Clear();

        _staticSpriteLock.Wait();
        _staticSprites.Clear();
        _staticSpriteLoads.Clear();
        _miniObjectSprites.Clear();

        _textureFrameSequenceLock.Wait();
        _textureFrameSequences.Clear();
        _textureFrameSequenceLoads.Clear();

        _playerAnimationLock.Wait();
        _playerCharacterAnimations.Clear();

        _modelLock.Wait();
        _grnModels.Clear();
        _grnModelLoads.Clear();
        _playerCharacters.Clear();

        _texturePak.Dispose();
        _modelTexturePak.Dispose();
        _modelsPak.Dispose();
    }

    private sealed record TextureCacheEntry(TextureAsset Asset, LinkedListNode<string> Node);

    private static string ModelCacheKey(string modelName, GrnMeshExtractionMode meshExtractionMode) =>
        $"{meshExtractionMode}:{modelName}";

    private static PlayerCharacterDefinition GetPlayerCharacterDefinition(uint entryId)
    {
        var definitionIndex = checked((int)entryId - 1);
        if ((uint)definitionIndex >= (uint)PlayerCharacterDefinitions.Length)
            throw new FileNotFoundException($"Player character slot {entryId} was not configured.");

        return PlayerCharacterDefinitions[definitionIndex];
    }

    private ItemsPakEntry ResolvePlayerCharacterItem(PlayerCharacterDefinition definition)
    {
        if (!_itemsByItemId.TryGetValue(definition.BaseItemId, out var items))
            throw new FileNotFoundException($"Player character item id {definition.BaseItemId} was not found in Items.pak.");

        foreach (var item in items)
        {
            if (string.Equals(item.ModelDesc.ModelName, definition.ModelName, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        throw new FileNotFoundException(
            $"Player character item id {definition.BaseItemId} does not reference model {definition.ModelName} in Items.pak.");
    }

    private ItemsPakEntry ResolvePlayerCharacterItem(uint itemId)
    {
        if (!_itemsByItemId.TryGetValue(itemId, out var items))
            throw new FileNotFoundException($"Player character item id {itemId} was not found in Items.pak.");

        var item = items[0];
        if (string.IsNullOrWhiteSpace(item.ModelDesc.ModelName))
            throw new FileNotFoundException($"Player character item id {itemId} does not reference a model in Items.pak.");

        return item;
    }

    private PlayerCharacterAttachmentItem[] ResolvePlayerCharacterItems(IReadOnlyList<PlayerCharacterAttachment> attachments)
    {
        var items = new PlayerCharacterAttachmentItem[attachments.Count];
        for (var i = 0; i < attachments.Count; i++)
            items[i] = new PlayerCharacterAttachmentItem(
                attachments[i],
                ResolvePlayerCharacterItem(attachments[i].ItemId));

        return items;
    }

    private IReadOnlyDictionary<string, ModelTextureReference> CreatePlayerCharacterTextureAliases(
        GrnAsset model,
        ItemsPakEntry baseItem,
        IReadOnlyList<PlayerCharacterAttachmentItem> attachmentItems,
        IReadOnlyList<GrnAsset> attachmentModels)
    {
        if (model.Mesh is null)
            return EmptyTextureAliases;

        var aliases = new Dictionary<string, ModelTextureReference>(StringComparer.OrdinalIgnoreCase);
        AddItemTextureAliases(aliases, model, baseItem);

        for (var index = 0; index < attachmentItems.Count; index++)
        {
            AddItemTextureAliases(aliases, attachmentModels[index], attachmentItems[index].Item);
        }

        return aliases.Count == 0 ? EmptyTextureAliases : aliases;
    }

    private EquipmentEffectAttachment[] CreateEquipmentEffectAttachments(
        IReadOnlyList<PlayerCharacterAttachmentItem> attachmentItems,
        IReadOnlyList<GrnAsset> attachmentModels)
    {
        var effects = new List<EquipmentEffectAttachment>();
        for (var index = 0; index < attachmentItems.Count; index++)
        {
            var attachmentItem = attachmentItems[index];
            if (!_equipmentByModelId.TryGetValue(attachmentItem.Item.ItemIndex, out var equipment))
                continue;

            var boundsSize = attachmentModels[index].Diagnostics?.WholeModelBounds is { } bounds
                ? Vector3.Distance(bounds.Min, bounds.Max)
                : 40.0f;
            effects.Add(new EquipmentEffectAttachment(
                index + 1,
                attachmentItem.Item.ModelDesc.ModelName,
                attachmentItem.Attachment.RigidAttachBoneName,
                equipment.Damage,
                boundsSize));
        }

        return effects.ToArray();
    }

    private void AddItemTextureAliases(
        Dictionary<string, ModelTextureReference> aliases,
        GrnAsset model,
        ItemsPakEntry item)
    {
        if (model.Mesh is null)
            return;

        var modelHasEffectTextureSurface = ModelHasEffectTextureSurface(model, item);
        var preferItemTexture = model.Mesh.Surfaces
            .Select(static surface => surface.TextureName)
            .Where(static textureName => !string.IsNullOrWhiteSpace(textureName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Count() == 1;
        foreach (var surface in model.Mesh.Surfaces)
        {
            if (string.IsNullOrWhiteSpace(surface.TextureName))
                continue;

            var reference = ModelTextureResolver.Resolve(
                _texturePak,
                item.ModelDesc.TextureId,
                item.EffectTextureId,
                item.GraphicRenderFlags,
                item.ModelDesc.EffectAnimationRate,
                modelHasEffectTextureSurface,
                preferItemTexture,
                surface.TextureName);

            if (!reference.Animation.IsAnimated &&
                !reference.HasOverlay &&
                string.Equals(reference.TextureName, surface.TextureName, StringComparison.OrdinalIgnoreCase))
                continue;

            aliases[surface.TextureName] = reference;
        }
    }

    private bool ModelHasEffectTextureSurface(GrnAsset model, ItemsPakEntry item)
    {
        if (model.Mesh is null ||
            item.EffectTextureId == 0 ||
            !_texturePak.TryGetTextureName(item.EffectTextureId, out var effectTextureName))
        {
            return false;
        }

        foreach (var surface in model.Mesh.Surfaces)
        {
            if (!string.IsNullOrWhiteSpace(surface.TextureName) &&
                _texturePak.TryResolveTextureName(surface.TextureName, out var resolvedName) &&
                string.Equals(resolvedName, effectTextureName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
        uint BaseItemId,
        string ModelName,
        string DisplayName,
        PlayerCharacterAttachment[] Attachments);

    private readonly record struct PlayerCharacterAttachment(
        uint ItemId,
        string? RigidAttachBoneName = null,
        string? SourceAttachBoneName = null);

    private readonly record struct PlayerCharacterAttachmentItem(
        PlayerCharacterAttachment Attachment,
        ItemsPakEntry Item);

    private readonly record struct StaticSpriteAssetKey(
        uint GroupId,
        int FrameCount,
        byte FrameDuration10Ms);
}
