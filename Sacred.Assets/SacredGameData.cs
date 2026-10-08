using System.Collections.Frozen;
using Sacred.Assets.GameBin.Sets;
using Sacred.Assets.GameBin.WeaponModifiers;
using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core;
using Sacred.Core.GameBin.Sets;
using Sacred.Core.GameBin.WeaponModifiers;
using Sacred.Core.GameRes;

namespace Sacred.Assets;

public class SacredGameData
{
    public GamePakStore GamePakStore { get; }
    public GameResStore GameResStore { get; }
    public IReadOnlyList<SacredSetEntry> ItemSets { get; private init; } = [];
    public IReadOnlyList<SacredWeaponModifier> WeaponModifiers { get; private init; } = [];

    private SacredGameData(GamePakStore gamePakStore, GameResStore gameResStore)
    {
        GamePakStore = gamePakStore;
        GameResStore = gameResStore;
    }

    public static SacredGameData LoadFromGamePaks(SacredGameDirectories gameDirectories)
    {
        var gamePakStore = LoadGamePakStore(gameDirectories);

        var gameResStore = LoadGameResStore(gameDirectories);

        var setsPath = gameDirectories.ItemSetsPath ?? Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(gameDirectories.WeaponsPakPath)))!, "bin", "sets.bin");
        var modifiersPath = gameDirectories.WeaponModifiersPath ?? Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(gameDirectories.WeaponsPakPath)))!, "bin", "wpmod.bin");
        var modifiers = File.Exists(modifiersPath) ? WeaponModifiersBinArchive.Load(modifiersPath) : [];
        Console.WriteLine($"Loaded weapon generation table: {modifiers.Count} definitions.");
        return new SacredGameData(gamePakStore, gameResStore)
        {
            ItemSets = File.Exists(setsPath) ? SetsBinArchive.Load(setsPath) : [],
            WeaponModifiers = modifiers
        };
    }

    private static GameResStore LoadGameResStore(SacredGameDirectories gameDirectories)
    {
        var strings = SacredResUnpack.UnpackAsDictionary(gameDirectories.GlobalResourcesPath);
        var gameResStore = new GameResStore(strings);
        return gameResStore;
    }

    private static GamePakStore LoadGamePakStore(SacredGameDirectories gameDirectories)
    {
        var items = ItemsPakArchive.Load(gameDirectories.ItemsPakPath)
            .ToFrozenDictionary(item => item.EntryInfo.ItemIndex);
        var weapons = WeaponPakParser.Parse(gameDirectories.WeaponsPakPath, items)
            .ToFrozenDictionary(item => item.IdemId);
        
        var gamePakStore = new GamePakStore(weapons, items);
        return gamePakStore;
    }
}
