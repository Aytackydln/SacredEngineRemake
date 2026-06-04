using System.Collections.Frozen;
using Sacred.Core;
using SacredItemSimulator.GamePak;
using SacredItemSimulator.GamePak.Loaders;
using SacredItemSimulator.GameRes;

namespace SacredItemSimulator;

public class SacredGameData
{
    public GamePakStore GamePakStore { get; }
    public GameResStore GameResStore { get; }
    
    private SacredGameData(GamePakStore gamePakStore, GameResStore gameResStore)
    {
        GamePakStore = gamePakStore;
        GameResStore = gameResStore;
    }

    public static SacredGameData LoadFromGamePaks(SacredGameDirectories gameDirectories)
    {
        var gamePakStore = LoadGamePakStore(gameDirectories);

        var gameResStore = LoadGameResStore(gameDirectories);

        return new SacredGameData(gamePakStore, gameResStore);
    }

    private static GameResStore LoadGameResStore(SacredGameDirectories gameDirectories)
    {
        var reverseIndexMap = SacredResUnpack.Unpack(gameDirectories.ReferenceResourcesPath)
            .DistinctBy(kv => kv.Value)
            .ToFrozenDictionary(kv => kv.Value, kv => kv.Key);

        var strings = SacredResUnpack.UnpackAsDictionary(gameDirectories.GlobalResourcesPath, gameDirectories.LocalResourcesPath);
        var gameResStore = new GameResStore(strings, reverseIndexMap);
        return gameResStore;
    }

    private static GamePakStore LoadGamePakStore(SacredGameDirectories gameDirectories)
    {
        var textureInfos = SacredTextureUnpacker.Extract(gameDirectories.TexturesPakPath)
            .DistinctBy(info => info.ImageInfo.FileName)
            .ToFrozenDictionary(info => info.ImageInfo.FileName, info => info);

        var items = ItemsPakParser.Parse(gameDirectories.ItemsPakPath)
            .ToFrozenDictionary(item => item.EntryInfo.ItemIndex);
        var weapons = WeaponPakParser.Parse(gameDirectories.WeaponsPakPath, items)
            .ToFrozenDictionary(item => item.IdemId);
        
        var gamePakStore = new GamePakStore(weapons, items, textureInfos);
        return gamePakStore;
    }
}