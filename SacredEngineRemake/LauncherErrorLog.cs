using System;
using System.IO;
using System.Text;

namespace SacredRemake;

internal static class LauncherErrorLog
{
    private const string FileName = "SacredEngineRemake.error.log";
    private static readonly object WriteLock = new();

    internal static void Write(string message)
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);
        try
        {
            lock (WriteLock)
            {
                File.AppendAllText(path,
                    $"[{DateTimeOffset.UtcNow:O}]{Environment.NewLine}{message}{Environment.NewLine}{Environment.NewLine}",
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch (Exception exception)
        {
            // Failure to write the log must not prevent displaying the original error.
            Console.Error.WriteLine($"Could not write error log '{path}': {exception.Message}");
            Console.Error.WriteLine(message);
        }
    }
}
