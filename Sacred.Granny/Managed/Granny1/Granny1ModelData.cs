using System.Numerics;
using Sacred.Granny.Animation;
using Sacred.Granny.Meshes;

namespace Sacred.Granny.Managed.Granny1;

public static partial class Granny1MeshExtractor
{
    private sealed record ParsedMeshSlice(
        ParsedMeshPart[] Parts,
        TexturePolygon[] TexturePolygons,
        TexturePolygonBlock[] TexturePolygonBlocks,
        string[] TextureNames,
        GrannySkeleton? Skeleton);

    private readonly record struct ParsedMeshPart(
        int SourceMeshIndex,
        int PointOffset,
        int PolygonOffset,
        Vector3[] Positions,
        Vector3[] Normals,
        Vector2[] TextureCoordinates,
        GrannyPolygon[] Polygons,
        VertexWeight[][] Weights,
        uint[] BoneTieBones,
        int[] TargetBoneIndices,
        int RigidBoneIndex);

    private readonly record struct VertexWeight(uint BoneTieIndex, float Weight);

    private readonly record struct BuiltMesh(Mesh Mesh, GrnMeshSkin? Skin);

    private enum SideRemap
    {
        None,
        RightToLeft,
        LeftToRight
    }

    private sealed record GrannySkeleton(
        GrannyBone[] Bones,
        uint[] BoneTieBones,
        IReadOnlyDictionary<string, int> BonesByName);

    private readonly record struct GrannyBone(
        string Name,
        int ParentIndex,
        Vector3 RestTranslation,
        Quaternion RestRotation,
        Matrix4x4 RestScaleShear,
        Matrix4x4 RestLocal,
        Matrix4x4 RestWorld);

    private readonly record struct GrannyPolygon(
        uint A,
        uint B,
        uint C,
        uint NormalA,
        uint NormalB,
        uint NormalC);

    private readonly record struct TexturePolygon(uint A, uint B, uint C, uint D);

    private sealed record TexturePolygonBlock(
        TexturePolygon[] Polygons,
        int FormSlot,
        int SourceMeshIndex,
        int TextureIndex);

    private sealed record FormMeshData(
        int[] SourceMeshMap,
        IReadOnlyDictionary<int, uint[]> BoneTieBonesBySourceMesh);

    private readonly record struct ItemDescriptor(
        uint Chunk,
        uint RelativeOffset,
        int DataOffset,
        int DescendantCount,
        int DescriptorOffset);

    private readonly record struct MeshKey(int PointOffset, int PolygonOffset);

    private readonly record struct Bounds(Vector3 Min, Vector3 Max);
}

