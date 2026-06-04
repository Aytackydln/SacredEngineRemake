using System;
using Sacred.Core;
using Sacred.Engine;

const string gameDir = @"E:\SteamLibrary\steamapps\common\Sacred Gold";

const string germanRes = gameDir + @"\scripts\de\SRglbl.res";

const string globalRes = gameDir + @"\scripts\us\global.res";
const string srGlobalRes = gameDir + @"\scripts\us\SRglbl.res";

const string weaponPak = gameDir + @"\pak\Weapon.pak";
const string itemsPak = gameDir + @"\pak\Items.pak";
const string texturePak = gameDir + @"\pak\Texture.pak";

var directories = new SacredGameDirectories
{
    GlobalResourcesPath = globalRes,
    LocalResourcesPath = srGlobalRes,
    ReferenceResourcesPath = germanRes,
    WeaponsPakPath = weaponPak,
    ItemsPakPath = itemsPak,
    TexturesPakPath = texturePak,
};

using var game = new SacredGame(directories);
game.Run();
Console.WriteLine("Game exited.");
