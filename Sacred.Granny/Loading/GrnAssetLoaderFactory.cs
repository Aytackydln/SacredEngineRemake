using Sacred.Granny.Abstractions;
using Sacred.Granny.Native;

namespace Sacred.Granny.Loading;

public static class GrnAssetLoaderFactory
{
    public static IGrnAssetLoader Create(
        GrnBackendKind backend,
        string gameDirectory,
        string? nativeLibraryPath = null) =>
        backend switch
        {
            GrnBackendKind.ManagedParser => ManagedGrnAssetLoader.Instance,
            GrnBackendKind.GrannyDll => new GrannyDllGrnAssetLoader(
                nativeLibraryPath ?? Path.Combine(gameDirectory, "granny_x64.dll")),
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown Granny backend.")
        };
}
