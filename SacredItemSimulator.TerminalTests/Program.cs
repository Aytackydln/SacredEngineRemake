// See https://aka.ms/new-console-template for more information

using Sacred.Assets;
using Sacred.Core;
using SacredItemSimulator.TerminalTests.Experiments;

const string gameDir = @"E:\SteamLibrary\steamapps\common\Sacred Gold";

const string globalRes = gameDir + @"\scripts\us\global.res";

const string weaponPak = gameDir + @"\pak\Weapon.pak";
const string itemsPak = gameDir + @"\pak\Items.pak";
const string texturePak = gameDir + @"\pak\Texture.pak";

var gameDirectories = new SacredGameDirectories
{
    GlobalResourcesPath = globalRes,
    WeaponsPakPath = weaponPak,
    ItemsPakPath = itemsPak,
    TexturesPakPath = texturePak,
};
var sacredGameData = SacredGameData.LoadFromGamePaks(gameDirectories);

// run experiments
ReadOnlySpan<IExperiment> experiments = [
    new ExpHealingPotion(),
    new ExpDragonShield(),
    new ExpPeekValues(),
    new ExpBlockedTiles(),
];
foreach (var experiment in experiments)
{
    experiment.Run(sacredGameData);
}
