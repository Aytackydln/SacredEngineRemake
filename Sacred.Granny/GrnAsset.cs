namespace Sacred.Granny;

public sealed record GrnAsset(
    string Name,
    byte[] RawBytes,
    string? ReferencedTexture,
    Mesh? Mesh
)
{
    public GrnModelDiagnostics? Diagnostics { get; init; }

    public GrnMeshSkin? Skin { get; init; }

    public GrnAnimationClip? DefaultAnimation { get; init; }
}
