using System.Numerics;
using Sacred.Granny.Animation;
using Sacred.Granny.Assets;

namespace Sacred.Granny.Abstractions;

public interface IGrnAssetLoader : IDisposable
{
    GrnBackendKind Kind { get; }

    string DisplayName { get; }

    GrnAsset LoadFromBytes(
        string name,
        byte[] bytes,
        GrnMeshExtractionMode meshExtractionMode = GrnMeshExtractionMode.PrimarySlice,
        Vector3? modelScale = null);

    GrnAsset LoadCharacterFromBytes(
        string name,
        byte[] baseBytes,
        IReadOnlyList<GrnCharacterAttachment> attachments,
        byte[]? defaultAnimationBytes = null,
        string? defaultAnimationName = null,
        Vector3? baseModelScale = null);

    GrnAnimationClip? TryExtractAnimation(
        byte[] bytes,
        string? name = null,
        Vector3? modelScale = null);
}
