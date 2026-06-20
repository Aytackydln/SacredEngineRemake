using System.Numerics;

namespace Sacred.Granny;

public static class GrnAssetLoader
{
    public static GrnAsset LoadFromBytes(
        string name,
        byte[] bytes,
        GrnMeshExtractionMode meshExtractionMode = GrnMeshExtractionMode.PrimarySlice,
        Vector3? modelScale = null)
    {
        var extraction = Granny1MeshExtractor.Extract(bytes, meshExtractionMode, modelScale);

        return new GrnAsset(name, bytes, null, extraction.Mesh)
        {
            Diagnostics = extraction.Diagnostics
        };
    }

    public static GrnAsset LoadCharacterFromBytes(
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
            DefaultAnimation = animation
        };
    }
}
