using System.Numerics;

namespace Sacred.Granny;

public sealed record GrnModelDiagnostics(
    IReadOnlyList<GrnSliceDiagnostics> Slices,
    GrnBoundsDiagnostics? WholeModelBounds,
    GrnBoundsDiagnostics? SkeletonBounds)
{
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
    int BoneTieCount);

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
    Vector3 Position);
