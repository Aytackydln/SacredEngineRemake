using Sacred.Granny.Abstractions;
using Sacred.Granny.Native;

namespace Sacred.Granny.Loading;

public static class GrnAssetLoaderFactory
{
    public static IGrnAssetLoader Create(
        GrnBackendKind backend,
        string gameDirectory,
        string? workerPath = null) =>
        backend switch
        {
            GrnBackendKind.ManagedParser => ManagedGrnAssetLoader.Instance,
            GrnBackendKind.GrannyDll => new GrannyDllGrnAssetLoader(
                Path.Combine(gameDirectory, "granny.dll"),
                workerPath ?? GrannyDllWorkerLocator.Find()),
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown Granny backend.")
        };
}
