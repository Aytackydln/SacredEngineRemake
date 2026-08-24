using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Mixed;
using Sacred.Assets.Paks.Models;
using Sacred.Assets.Paks.Texture;
using Sacred.Assets.Paks.Tiles;
using Sacred.Assets.Paks.Weapon;
using Sacred.Assets.World.Floor;
using Sacred.Assets.World.Static;
using Sacred.Core;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon;
using Sacred.Core.World.Stairs;
using Sacred.Granny.Abstractions;
using Sacred.Granny.Loading;
using Sacred.World;

namespace Sacred.Engine.Assets;

/// <summary>
/// Opens the original game resources in explicit loading-screen stages. Until ownership is
/// transferred to the runtime, this object also guarantees cleanup after a failed load.
/// </summary>
internal sealed class GameResourceLoader : IDisposable
{
    private readonly SacredGameDirectories _directories;
    private readonly string _gameDirectory;
    private readonly string _pakDirectory;
    private readonly string _worldDirectory;
    private readonly GrnBackendKind _grannyBackend;

    private TexturePakArchive? _texturePak;
    private ItemsPakEntry[]? _items;
    private FrozenDictionary<ushort, ItemsPakEntry>? _itemsByModelId;
    private ModelsPakArchive? _modelsPak;
    private StaticPakArchive? _staticPak;
    private MixedPakArchive? _mixedPak;
    private TilesPakArchive? _tilesPak;
    private SacredEquipment[]? _equipment;
    private byte[]? _keyxData;
    private SacredStairsMap? _stairsMap;
    private FileStream? _wldxStream;
    private FloorPakArchive? _floorPak;
    private SacredWorldArchive? _worldArchive;
    private bool _ownershipTransferred;
    private bool _disposed;

    public GameResourceLoader(
        SacredGameDirectories directories,
        GrnBackendKind grannyBackend = GrnBackendKind.ManagedParser)
    {
        _directories = directories ?? throw new ArgumentNullException(nameof(directories));
        _grannyBackend = grannyBackend;
        _pakDirectory = Path.GetDirectoryName(directories.TexturesPakPath)
            ?? throw new InvalidDataException("Cannot infer the PAK directory from Texture.pak.");
        _gameDirectory = Directory.GetParent(_pakDirectory)?.FullName
            ?? throw new InvalidDataException("Cannot infer the game directory from Texture.pak.");
        _worldDirectory = Path.Combine(_gameDirectory, "World");
    }

    public string PakDirectory => _pakDirectory;

    public IReadOnlyList<ResourceLoadStep> CreateInitialLoadSteps() =>
    [
        new("Texture.pak", LoadTexturePak),
        new("Items.pak", LoadItemsPak),
        new("Models.pak + Models.tmp", LoadModelsPak),
        new("Static.pak", LoadStaticPak),
        new("Mixed.pak", LoadMixedPak)
    ];

    public IReadOnlyList<ResourceLoadStep> CreateGameLoadSteps() =>
    [
        new("Tiles.pak", LoadTilesPak),
        new("Weapons.pak", LoadWeaponsPak),
        new("stairs data", LoadStairsMap),
        new("sectors.keyx", LoadKeyx),
        new("sectors.wldx", OpenWldx),
        new("Floor.pak", LoadFloorPak),
        new("world sectors", LoadWorldArchive)
    ];

    public LoadedGameResources TransferToRuntime()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_ownershipTransferred)
            throw new InvalidOperationException("Game resources were already transferred to the runtime.");

        var texturePak = Require(_texturePak, "Texture.pak");
        var items = Require(_items, "Items.pak");
        var modelsPak = Require(_modelsPak, "Models.pak");
        var mixedPak = Require(_mixedPak, "Mixed.pak");
        var tilesPak = Require(_tilesPak, "Tiles.pak");
        var equipment = Require(_equipment, "Weapons.pak");
        var worldArchive = Require(_worldArchive, "world sectors");

        AssetManager? assets = null;
        SacredWorldArchive? world = null;
        try
        {
            assets = new AssetManager(texturePak, tilesPak, items, equipment, mixedPak, modelsPak);
            world = worldArchive;
            _ownershipTransferred = true;
            ReleaseTransferredReferences();
            return new LoadedGameResources(assets, world);
        }
        catch
        {
            assets?.Dispose();
            world?.Dispose();
            throw;
        }
    }

    private void LoadTexturePak() => _texturePak = TexturePakArchive.LoadFromDirectory(_pakDirectory);

    private void LoadItemsPak()
    {
        _items = ItemsPakArchive.Load(_directories.ItemsPakPath).ToArray();
        _itemsByModelId = _items.ToFrozenDictionary(static item => item.ItemIndex);
    }

    private void LoadModelsPak()
    {
        var assetLoader = GrnAssetLoaderFactory.Create(_grannyBackend, _gameDirectory);
        try
        {
            _modelsPak = ModelsPakArchive.Load(
                Path.Combine(_pakDirectory, "models.pak"),
                Path.Combine(_pakDirectory, "Models.tmp"),
                assetLoader);
        }
        catch
        {
            assetLoader.Dispose();
            throw;
        }
    }

    private void LoadStaticPak() => _staticPak = StaticPakArchive.Load(Path.Combine(_worldDirectory, "Static.pak"));

    private void LoadMixedPak() => _mixedPak = MixedPakArchive.Load(Path.Combine(_pakDirectory, "mixed.pak"));

    private void LoadTilesPak() => _tilesPak = TilesPakArchive.Load(Path.Combine(_pakDirectory, "tiles.pak"));

    private void LoadWeaponsPak()
    {
        var itemsByModelId = Require(_itemsByModelId, "Items.pak");
        _equipment = WeaponPakParser.Parse(_directories.WeaponsPakPath, itemsByModelId).ToArray();
        _itemsByModelId = null;
    }

    private void LoadKeyx() => _keyxData = File.ReadAllBytes(Path.Combine(_worldDirectory, "sectors.keyx"));

    private void LoadStairsMap() => _stairsMap = SacredStairsMap.Load(
        _directories.StairsMapPath ?? Path.Combine(_gameDirectory, "bin", "treppe.bin"),
        _directories.DefPosPath ?? Path.Combine(_gameDirectory, "bin", "NetScript", "DefPos.bin"));

    private void OpenWldx() => _wldxStream = new FileStream(
        Path.Combine(_worldDirectory, "sectors.wldx"),
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 1,
        FileOptions.Asynchronous | FileOptions.RandomAccess);

    private void LoadFloorPak() => _floorPak = FloorPakArchive.Load(Path.Combine(_worldDirectory, "Floor.pak"));

    private void LoadWorldArchive()
    {
        _worldArchive = SacredWorldArchiveFactory.Create(
            Require(_keyxData, "sectors.keyx"),
            Require(_wldxStream, "sectors.wldx"),
            Require(_floorPak, "Floor.pak"),
            Require(_staticPak, "Static.pak"),
            Require(_stairsMap, "stairs data"));

        _keyxData = null;
        _wldxStream = null;
        _floorPak = null;
        _staticPak = null;
        _stairsMap = null;
    }

    private static T Require<T>(T? value, string resourceName) where T : class =>
        value ?? throw new InvalidOperationException($"{resourceName} has not been loaded.");

    private void ReleaseTransferredReferences()
    {
        _texturePak = null;
        _items = null;
        _itemsByModelId = null;
        _modelsPak = null;
        _staticPak = null;
        _mixedPak = null;
        _tilesPak = null;
        _equipment = null;
        _stairsMap = null;
        _keyxData = null;
        _wldxStream = null;
        _floorPak = null;
        _worldArchive = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_ownershipTransferred)
            return;

        _wldxStream?.Dispose();
        _floorPak?.Dispose();
        _staticPak?.Dispose();
        _worldArchive?.Dispose();
        _modelsPak?.Dispose();
        _texturePak?.Dispose();
    }
}

internal sealed record ResourceLoadStep(string DisplayName, Action Load);

internal sealed record LoadedGameResources(AssetManager Assets, SacredWorldArchive WorldArchive);
