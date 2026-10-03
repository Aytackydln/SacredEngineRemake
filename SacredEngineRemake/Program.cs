using System;
using System.IO;
using System.Linq;
using Sacred.Core;
using Sacred.Engine;
using SacredRemake;
using Serilog;

var terminalMode = args.Any(LaunchArguments.IsTerminalMode);
CrashReporter.Register(terminalMode);

try
{
    WindowsApplicationIdentity.Initialize();

    if (terminalMode)
        TerminalWindow.Open();

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    if (args.Any(argument => string.Equals(argument, "-test-error", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Error reporting test: SacredEngineRemake.error.log should contain this exception and its stack trace.");
    }

    var gameDir = args.FirstOrDefault(argument => !LaunchArguments.IsTerminalMode(argument))
                  ?? @"E:\SteamLibrary\steamapps\common\Sacred Gold";

    if (!Directory.Exists(gameDir))
        gameDir = ".";

    var pakDir = Path.Combine(gameDir, "pak");
    var scriptsDir = Path.Combine(gameDir, "scripts");

    if (!Directory.Exists(pakDir) || !Directory.Exists(scriptsDir))
    {
        Environment.ExitCode = 1;
        LauncherError.Show("Pak directory does not exist or could not be found.", terminalMode);
        return;
    }

    var directories = new SacredGameDirectories
    {
        GlobalResourcesPath = Path.Combine(scriptsDir, "us", "global.res"),
        StairsMapPath = Path.Combine(gameDir, "bin", "treppe.bin"),
        DefPosPath = Path.Combine(gameDir, "bin", "NetScript", "DefPos.bin"),
        WeaponsPakPath = Path.Combine(pakDir, "Weapon.pak"),
        ItemsPakPath = Path.Combine(pakDir, "Items.pak"),
        TexturesPakPath = Path.Combine(pakDir, "Texture.pak"),
    };

    var saveState = SacredEngineRemakeConfig.Load(gameDir);

    using (var game = new SacredGame(directories, saveState))
    {
        try
        {
            await game.Run();
        }
        finally
        {
            SacredEngineRemakeConfig.Save(gameDir, game.CaptureSaveState());
        }
    }

    if (terminalMode)
        Log.Information("Game exited");
}
catch (Exception exception) when (CrashReporter.Report(exception, terminalMode))
{
    // The filter reports the original failure before config capture and GPU disposal.
}
finally
{
    Log.CloseAndFlush();
}
