using System;
using System.IO;
using Sacred.Core;
using Sacred.Engine;

var arg1 = args.Length > 0 ? args[0] : null;

var gameDir = arg1 ?? @"E:\SteamLibrary\steamapps\common\Sacred Gold";

if (!Directory.Exists(gameDir))
{
    gameDir = ".";
}

var pakDir = Path.Combine(gameDir, "pak");
var scriptsDir = Path.Combine(gameDir, "scripts");

if (!Directory.Exists(pakDir) || !Directory.Exists(scriptsDir))
{
    Console.WriteLine("Pak directory does not exist or could not be found.");
    Console.WriteLine("Press enter to exit.");
    await Console.In.ReadLineAsync();
    return;
}

var germanRes = gameDir + @"\scripts\de\SRglbl.res";

var globalRes = gameDir + @"\scripts\us\global.res";
var srGlobalRes = gameDir + @"\scripts\us\SRglbl.res";

var weaponPak = gameDir + @"\pak\Weapon.pak";
var itemsPak = gameDir + @"\pak\Items.pak";
var texturePak = gameDir + @"\pak\Texture.pak";

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
