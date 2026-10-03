using System;
using System.Threading;
using Serilog;

namespace SacredRemake;

internal static class CrashReporter
{
    private static int _reported;

    internal static void Register(bool terminalMode)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            Report(eventArgs.ExceptionObject as Exception ??
                   new Exception(eventArgs.ExceptionObject?.ToString() ?? "Unknown unhandled exception."),
                terminalMode);
    }

    // Used as an exception filter so the dialog appears before finally/Dispose runs.
    internal static bool Report(Exception exception, bool terminalMode)
    {
        Environment.ExitCode = 1;
        if (Interlocked.Exchange(ref _reported, 1) != 0)
            return true;

        var message = $"The game terminated unexpectedly.{Environment.NewLine}{Environment.NewLine}{exception}";
        LauncherError.Show(message, terminalMode);

        try
        {
            Log.Fatal(exception, "The game terminated unexpectedly");
        }
        catch (Exception)
        {
            // A logging failure must not replace the original exception.
        }

        return true;
    }
}
