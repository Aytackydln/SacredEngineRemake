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

    Log.Information("Runtime executable: {Executable}; application directory: {Directory}",
        Environment.ProcessPath, AppContext.BaseDirectory);
    var engineAssemblyPath = Path.Combine(AppContext.BaseDirectory, "Sacred.Engine.dll");
    if (File.Exists(engineAssemblyPath))
        Log.Information("Engine assembly: {Path}; modified UTC: {Modified:O}",
            engineAssemblyPath, File.GetLastWriteTimeUtc(engineAssemblyPath));

    if (args.Any(argument => string.Equals(argument, "-test-error", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Error reporting test: SacredEngineRemake.error.log should contain this exception and its stack trace.");
    }

    var serverEndpoint = args.FirstOrDefault(argument => argument.StartsWith("--server=", StringComparison.OrdinalIgnoreCase))?[9..];
    var serverCharacter = args.FirstOrDefault(argument => argument.StartsWith("--server-character=", StringComparison.OrdinalIgnoreCase))?[19..];
    var gameDir = args.FirstOrDefault(argument => !LaunchArguments.IsTerminalMode(argument) &&
                                                !argument.StartsWith("--", StringComparison.OrdinalIgnoreCase))
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
        WeaponsPakPath = Path.Combine(pakDir, "Weapon.pak"),
        ItemsPakPath = Path.Combine(pakDir, "Items.pak"),
        TexturesPakPath = Path.Combine(pakDir, "Texture.pak"),
    };

    var saveState = SacredEngineRemakeConfig.Load(gameDir);
    if(args.Contains("--no-focus")) saveState=saveState with { RequestWindowFocus=false, BorderlessFullscreen=false, WindowedMaximized=false };

    using (var game = new SacredGame(directories, saveState))
    {
        if (serverEndpoint is not null)
        {
            if(serverCharacter is not null && int.TryParse(serverCharacter,out var selection)) game.RequestServerJoin(serverEndpoint,selection);
            else game.RequestServerConnection(serverEndpoint);
        }
        try
        {
            await game.Run();
        }
        finally
        {
            if(serverEndpoint is null) SacredEngineRemakeConfig.Save(gameDir, game.CaptureSaveState());
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
