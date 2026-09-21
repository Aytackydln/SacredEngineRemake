using System.Numerics;
using Sacred.Granny.Abstractions;
using Sacred.Granny.Animation;
using Sacred.Granny.Assets;
using Sacred.Granny.Loading;
using Sacred.Granny.Managed.Granny1;

namespace Sacred.Granny.Native;

public sealed class GrannyDllGrnAssetLoader : IGrnAssetLoader
{
    private readonly GrannyX64NativeApi _native;
    private bool _disposed;

    public GrannyDllGrnAssetLoader(string nativeLibraryPath)
    {
        _native = new GrannyX64NativeApi(nativeLibraryPath);
    }

    public GrnBackendKind Kind => GrnBackendKind.GrannyDll;

    public string DisplayName => "granny_x64.dll (Granny 1.2b)";

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
                BackendDetail = "The granny_x64.dll backend currently delegates composite-slice extraction to the managed parser."
            };
        }

        var managedExtraction = Granny1MeshExtractor.Extract(bytes, meshExtractionMode, modelScale);
        try
        {
            var nativeData = GrannyX64MeshReader.Read(_native, bytes);
            var mesh = GrannyDllMeshBuilder.Build(nativeData, managedExtraction.Mesh, modelScale);
            return new GrnAsset(name, bytes, null, mesh)
            {
                Skin = managedExtraction.Skin,
                Diagnostics = managedExtraction.Diagnostics with
                {
                    SourceOriginOffset = GrannyDllMeshBuilder.GetSourceOriginOffset(nativeData, modelScale)
                },
                Backend = Kind,
                BackendDetail =
                    "Geometry, indices, UVs, and surface ranges came from granny_x64.dll; " +
                    "texture names were matched to managed GRN materials by triangle geometry and UVs."
            };
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or IOException)
        {
            // A model that the x64 renderer cannot materialize must remain
            // available to the game. The managed reader is data-driven and
            // keeps the scene complete while reporting the native limitation.
            return new GrnAsset(name, bytes, null, managedExtraction.Mesh)
            {
                Skin = managedExtraction.Skin,
                Diagnostics = managedExtraction.Diagnostics,
                Backend = GrnBackendKind.ManagedParser,
                BackendDetail = $"granny_x64.dll could not render this model: {exception.Message}"
            };
        }
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
                "the granny_x64.dll rendering path is used for standalone models."
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
        _native.Dispose();
    }
}
