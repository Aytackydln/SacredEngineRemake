using Sacred.Granny.Abstractions;
using Sacred.Granny.Animation;
using Sacred.Granny.Meshes;

namespace Sacred.Granny.Assets;

public sealed record GrnAsset(
    string Name,
    byte[] RawBytes,
    string? ReferencedTexture,
    Mesh? Mesh
)
{
    public GrnBackendKind Backend { get; init; } = GrnBackendKind.ManagedParser;

    public string? BackendDetail { get; init; }

    public GrnModelDiagnostics? Diagnostics { get; init; }

    public GrnMeshSkin? Skin { get; init; }

    public GrnAnimationClip? DefaultAnimation { get; init; }
}
