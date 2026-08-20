using Serilog;

namespace Sacred.Engine.Diagnostics;

/// <summary>Provides structured console logging for engine diagnostics.</summary>
public static class EngineLog
{
    public static void WriteLine(string message) => Log.Information("{Message}", message);
}
