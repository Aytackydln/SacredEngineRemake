using System.Numerics;
using Sacred.Granny.Abstractions;
using Sacred.Granny.Assets;

namespace Sacred.Granny.Loading;

public static class GrnAssetLoader
{
    public static GrnAsset LoadFromBytes(
        string name,
        byte[] bytes,
        GrnMeshExtractionMode meshExtractionMode = GrnMeshExtractionMode.PrimarySlice,
        Vector3? modelScale = null)
        => ManagedGrnAssetLoader.Instance.LoadFromBytes(name, bytes, meshExtractionMode, modelScale);

    public static GrnAsset LoadCharacterFromBytes(
        string name,
        byte[] baseBytes,
        IReadOnlyList<GrnCharacterAttachment> attachments,
        byte[]? defaultAnimationBytes = null,
        string? defaultAnimationName = null,
        Vector3? baseModelScale = null)
        => ManagedGrnAssetLoader.Instance.LoadCharacterFromBytes(
            name,
            baseBytes,
            attachments,
            defaultAnimationBytes,
            defaultAnimationName,
            baseModelScale);
}
