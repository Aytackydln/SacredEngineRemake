using System.Numerics;
using Sacred.Granny.Abstractions;
using Sacred.Granny.Animation;
using Sacred.Granny.Assets;
using Sacred.Granny.Loading;
using Sacred.Granny.Managed.Granny1;

namespace Sacred.Granny.Native;

public sealed class GrannyDllGrnAssetLoader : IGrnAssetLoader
{
    private readonly GrannyDllWorkerProcess _worker;
    private bool _disposed;

    public GrannyDllGrnAssetLoader(string grannyDllPath, string workerPath)
    {
        _worker = new GrannyDllWorkerProcess(grannyDllPath, workerPath);
    }

    public GrnBackendKind Kind => GrnBackendKind.GrannyDll;

    public string DisplayName => "Game Granny.dll (1.2b)";

    public GrnAsset LoadFromBytes(
        string name,
        byte[] bytes,
        GrnMeshExtractionMode meshExtractionMode = GrnMeshExtractionMode.PrimarySlice,
        Vector3? modelScale = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (meshExtractionMode != GrnMeshExtractionMode.PrimarySlice)
        {
            return ManagedGrnAssetLoader.Instance
                .LoadFromBytes(name, bytes, meshExtractionMode, modelScale) with
            {
                BackendDetail = "The Granny.dll backend currently delegates composite-slice extraction to the managed parser."
            };
        }

        var managedExtraction = Granny1MeshExtractor.Extract(bytes, meshExtractionMode, modelScale);
        var nativeData = _worker.Extract(bytes);
        var mesh = GrannyDllMeshBuilder.Build(nativeData, managedExtraction.Mesh, modelScale);
        return new GrnAsset(name, bytes, null, mesh)
        {
            Diagnostics = managedExtraction.Diagnostics,
            Backend = Kind,
            BackendDetail =
                "Geometry, indices, UVs, and surface ranges came from the game's 32-bit Granny.dll; " +
                "texture names were matched to managed GRN materials by triangle geometry and UVs."
        };
    }

    public GrnAsset LoadCharacterFromBytes(
        string name,
        byte[] baseBytes,
        IReadOnlyList<GrnCharacterAttachment> attachments,
        byte[]? defaultAnimationBytes = null,
        string? defaultAnimationName = null,
        Vector3? baseModelScale = null) =>
        ManagedGrnAssetLoader.Instance
            .LoadCharacterFromBytes(
                name,
                baseBytes,
                attachments,
                defaultAnimationBytes,
                defaultAnimationName,
                baseModelScale) with
        {
            BackendDetail =
                "Character composition and editable animation tracks currently use the managed parser; " +
                "the Granny.dll rendering path is used for standalone models."
        };

    public GrnAnimationClip? TryExtractAnimation(
        byte[] bytes,
        string? name = null,
        Vector3? modelScale = null) =>
        ManagedGrnAssetLoader.Instance.TryExtractAnimation(bytes, name, modelScale);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _worker.Dispose();
    }
}
