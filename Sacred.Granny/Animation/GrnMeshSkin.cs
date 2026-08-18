using System.Numerics;

namespace Sacred.Granny.Animation;

public sealed class GrnMeshSkin
{
    private readonly Matrix4x4[] _inverseBindTransforms;
    private readonly Matrix4x4[] _inverseRigidBindTransforms;

    internal GrnMeshSkin(
        GrnSkeleton skeleton,
        GrnSkinVertex[] vertices,
        GrnMeshProjection projection)
    {
        Skeleton = skeleton;
        Vertices = vertices;
        Projection = projection;
        _inverseBindTransforms = new Matrix4x4[skeleton.Bones.Length];
        _inverseRigidBindTransforms = new Matrix4x4[skeleton.Bones.Length];
        for (var index = 0; index < _inverseBindTransforms.Length; index++)
        {
            _inverseBindTransforms[index] = Matrix4x4.Invert(
                skeleton.Bones[index].RestWorld,
                out var inverse)
                ? inverse
                : Matrix4x4.Identity;
            _inverseRigidBindTransforms[index] = Matrix4x4.Invert(
                GrnRigidTransform.CreateOrOriginal(skeleton.Bones[index].RestWorld),
                out var inverseRigid)
                ? inverseRigid
                : Matrix4x4.Identity;
        }
    }

    public GrnSkeleton Skeleton { get; }

    internal GrnSkinVertex[] Vertices { get; }

    internal GrnMeshProjection Projection { get; }

    internal Matrix4x4 GetInverseBindTransform(int boneIndex) =>
        _inverseBindTransforms[boneIndex];

    internal Matrix4x4 GetInverseRigidBindTransform(int boneIndex) =>
        _inverseRigidBindTransforms[boneIndex];
}

internal readonly record struct GrnSkinVertex(
    Vector3 BindPosition,
    Vector3 BindNormal,
    GrnBoneWeight[] Weights,
    bool UsesRigidBoneTransform = false);

internal readonly record struct GrnBoneWeight(int BoneIndex, float Weight);

internal readonly record struct GrnMeshProjection(
    Vector3 Minimum,
    Vector3 Center,
    int VerticalAxis,
    int HorizontalAxis0,
    int HorizontalAxis1)
{
    public Vector3 Project(Vector3 source) => new(
        Axis(source, HorizontalAxis0) - Axis(Center, HorizontalAxis0),
        Axis(source, HorizontalAxis1) - Axis(Center, HorizontalAxis1),
        Axis(source, VerticalAxis) - Axis(Minimum, VerticalAxis));

    public Vector3 ProjectDirection(Vector3 source) => new(
        Axis(source, HorizontalAxis0),
        Axis(source, HorizontalAxis1),
        Axis(source, VerticalAxis));

    public Vector3 Unproject(Vector3 projected)
    {
        var result = Vector3.Zero;
        SetAxis(ref result, HorizontalAxis0, projected.X + Axis(Center, HorizontalAxis0));
        SetAxis(ref result, HorizontalAxis1, projected.Y + Axis(Center, HorizontalAxis1));
        SetAxis(ref result, VerticalAxis, projected.Z + Axis(Minimum, VerticalAxis));
        return result;
    }

    public Vector3 UnprojectDirection(Vector3 projected)
    {
        var result = Vector3.Zero;
        SetAxis(ref result, HorizontalAxis0, projected.X);
        SetAxis(ref result, HorizontalAxis1, projected.Y);
        SetAxis(ref result, VerticalAxis, projected.Z);
        return result;
    }

    private static float Axis(Vector3 value, int axis) => axis switch
    {
        0 => value.X,
        1 => value.Y,
        _ => value.Z
    };

    private static void SetAxis(ref Vector3 value, int axis, float component)
    {
        switch (axis)
        {
            case 0: value.X = component; break;
            case 1: value.Y = component; break;
            default: value.Z = component; break;
        }
    }
}
