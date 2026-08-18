namespace Sacred.Granny.Native;

internal static class GrannyDllWorkerLocator
{
    private const string WorkerName = "Sacred.Granny.Native.Worker.exe";

    public static string Find()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "GrannyNative", WorkerName),
            Path.Combine(AppContext.BaseDirectory, WorkerName)
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            $"The x86 Granny worker was not found. Expected '{candidates[0]}'. " +
            "Build or publish the ItemViewer/SacredEngineRemake executable so its GrannyNative folder is generated.",
            candidates[0]);
    }
}
