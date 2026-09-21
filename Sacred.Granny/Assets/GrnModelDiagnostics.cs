using System.Numerics;

namespace Sacred.Granny.Assets;

public sealed record GrnModelDiagnostics(
    IReadOnlyList<GrnSliceDiagnostics> Slices,
    GrnBoundsDiagnostics? WholeModelBounds,
    GrnBoundsDiagnostics? SkeletonBounds)
{
    /// <summary>Translation to restore authored coordinates from the centered, grounded mesh.</summary>
    public Vector3 SourceOriginOffset { get; init; }

    public int PartCount => Slices.Sum(static slice => slice.Parts.Count);
    public int BoneCount => Slices.Sum(static slice => slice.Bones.Count);
}

public readonly record struct GrnBoundsDiagnostics(Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) * 0.5f;
}

public sealed record GrnSliceDiagnostics(
    int Index,
    IReadOnlyList<GrnMeshPartDiagnostics> Parts,
    IReadOnlyList<string> TextureNames,
    int TexturePolygonCount,
    int TexturePolygonGroupCount,
    IReadOnlyList<GrnBoneDiagnostics> Bones,
    int BoneTieCount,
    IReadOnlyList<GrnSurfaceTriangleDiagnostics> SurfaceTriangles);

public readonly record struct GrnSurfaceTriangleDiagnostics(
    Vector3 A,
    Vector3 B,
    Vector3 C,
    Vector3 Normal,
    float Area);

public sealed record GrnMeshPartDiagnostics(
    int Index,
    int VertexCount,
    int PolygonCount,
    int TextureCoordinateCount,
    int WeightedVertexCount,
    int WeightCount);

public sealed record GrnBoneDiagnostics(
    int Index,
    string Name,
    int ParentIndex,
    Vector3 Position)
{
    /// <summary>Animated ancestor carrying this attachment after equipment retargeting.</summary>
    public string? AnimationBoneName { get; init; }
    /// <summary>Bone-local +X expressed in the projected model rest space.</summary>
    public Vector3 Direction { get; init; } = Vector3.UnitX;
}
