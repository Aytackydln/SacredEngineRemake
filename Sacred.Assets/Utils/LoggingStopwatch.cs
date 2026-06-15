using System.Diagnostics;

namespace Sacred.Assets.Utils;

public sealed class LoggingStopwatch : IDisposable
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public LoggingStopwatch(string startLog)
    {
        Console.Write(startLog);
    }

    public void Dispose()
    {
        Console.WriteLine(_stopwatch.Elapsed);
        _stopwatch.Stop();
    }
}