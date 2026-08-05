using System;
using System.IO;
using System.Linq;
using Sacred.Core;
using Sacred.Engine;
using SacredRemake;

var terminalMode = args.Any(LaunchArguments.IsTerminalMode);
if (terminalMode)
{
    TerminalWindow.Open();
}

var gameDir = args.FirstOrDefault(argument => !LaunchArguments.IsTerminalMode(argument))
              ?? @"E:\SteamLibrary\steamapps\common\Sacred Gold";

if (!Directory.Exists(gameDir))
{
    gameDir = ".";
}

var pakDir = Path.Combine(gameDir, "pak");
var scriptsDir = Path.Combine(gameDir, "scripts");

if (!Directory.Exists(pakDir) || !Directory.Exists(scriptsDir))
{
    LauncherError.Show("Pak directory does not exist or could not be found.", terminalMode);
    return;
}

var globalRes = gameDir + @"\scripts\us\global.res";

var weaponPak = gameDir + @"\pak\Weapon.pak";
var itemsPak = gameDir + @"\pak\Items.pak";
var texturePak = gameDir + @"\pak\Texture.pak";

var directories = new SacredGameDirectories
{
    GlobalResourcesPath = globalRes,
    WeaponsPakPath = weaponPak,
    ItemsPakPath = itemsPak,
    TexturesPakPath = texturePak,
};

try
{
    using var game = new SacredGame(directories);
    await game.Run();
}
catch (Exception e)
{
    Console.WriteLine(e);
    LauncherError.Show(e.ToString(), terminalMode);
    await Console.In.ReadLineAsync();
}

if (terminalMode)
{
    Console.WriteLine("Game exited.");
}
