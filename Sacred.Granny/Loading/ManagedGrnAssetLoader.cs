using System.Numerics;
using Sacred.Granny.Abstractions;
using Sacred.Granny.Animation;
using Sacred.Granny.Assets;
using Sacred.Granny.Managed.Granny1;

namespace Sacred.Granny.Loading;

public sealed class ManagedGrnAssetLoader : IGrnAssetLoader
{
    public static ManagedGrnAssetLoader Instance { get; } = new();

    private ManagedGrnAssetLoader()
    {
    }

    public GrnBackendKind Kind => GrnBackendKind.ManagedParser;

    public string DisplayName => "Managed Granny 1 parser";

    public GrnAsset LoadFromBytes(
        string name,
        byte[] bytes,
        GrnMeshExtractionMode meshExtractionMode = GrnMeshExtractionMode.PrimarySlice,
        Vector3? modelScale = null)
    {
        var extraction = Granny1MeshExtractor.Extract(bytes, meshExtractionMode, modelScale);
        return new GrnAsset(name, bytes, null, extraction.Mesh)
        {
            Diagnostics = extraction.Diagnostics,
            Backend = Kind
        };
    }

    public GrnAsset LoadCharacterFromBytes(
        string name,
        byte[] baseBytes,
        IReadOnlyList<GrnCharacterAttachment> attachments,
        byte[]? defaultAnimationBytes = null,
        string? defaultAnimationName = null,
        Vector3? baseModelScale = null)
    {
        var extraction = Granny1MeshExtractor.ExtractCharacter(
            baseBytes,
            attachments,
            baseModelScale);
        var animation = defaultAnimationBytes is null
            ? null
            : Granny1MeshExtractor.TryExtractAnimation(
                defaultAnimationBytes,
                defaultAnimationName,
                baseModelScale);

        return new GrnAsset(name, baseBytes, null, extraction.Mesh)
        {
            Skin = extraction.Skin,
            Diagnostics = extraction.Diagnostics,
            DefaultAnimation = animation,
            Backend = Kind
        };
    }

    public GrnAnimationClip? TryExtractAnimation(
        byte[] bytes,
        string? name = null,
        Vector3? modelScale = null) =>
        Granny1MeshExtractor.TryExtractAnimation(bytes, name, modelScale);

    public void Dispose()
    {
    }
}
