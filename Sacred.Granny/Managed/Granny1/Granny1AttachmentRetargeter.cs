using System.Numerics;

namespace Sacred.Granny.Managed.Granny1;

public static partial class Granny1MeshExtractor
{
    private static BoneAttachment[] RetargetAttachments(GrannySkeleton source, GrannySkeleton target)
    {
        var result = new BoneAttachment[source.Bones.Length];
        for (var index = 0; index < result.Length; index++)
        {
            var bone = source.Bones[index];
            var boneRotation = RestRotationWorld(source, index);
            result[index] = new BoneAttachment(
                bone.RestWorld,
                null,
                Vector3.TransformNormal(Vector3.UnitX, boneRotation));
            // Effect-only bones need not exist in the character skeleton. Preserve their
            // local offset from the nearest shared ancestor, just as for the equipment mesh.
            var ancestor = index;
            for (var remaining = source.Bones.Length; remaining > 0 && (uint)ancestor < source.Bones.Length; remaining--)
            {
                var parent = source.Bones[ancestor];
                if (TryFindAttachmentTarget(source, target, parent, out var targetIndex) &&
                    Matrix4x4.Invert(parent.RestWorld, out var inverseRest))
                {
                    var ancestorRotation = RestRotationWorld(source, ancestor);
                    var targetRotation = RestRotationWorld(target, targetIndex);
                    Matrix4x4.Invert(ancestorRotation, out var inverseAncestorRotation);
                    result[index] = new BoneAttachment(
                        bone.RestWorld * inverseRest * target.Bones[targetIndex].RestWorld,
                        target.Bones[targetIndex].Name,
                        Vector3.TransformNormal(
                            Vector3.UnitX,
                            boneRotation * inverseAncestorRotation * targetRotation));
                    break;
                }
                ancestor = parent.ParentIndex;
            }
        }
        return result;
    }

    private static Matrix4x4 RestRotationWorld(GrannySkeleton skeleton, int index)
    {
        var result = Matrix4x4.Identity;
        for (var remaining = skeleton.Bones.Length;
             remaining > 0 && (uint)index < skeleton.Bones.Length;
             remaining--)
        {
            var bone = skeleton.Bones[index];
            result *= Matrix4x4.CreateFromQuaternion(bone.RestRotation);
            if (bone.ParentIndex == index)
                break;
            index = bone.ParentIndex;
        }
        return result;
    }

    private static bool TryFindAttachmentTarget(GrannySkeleton source, GrannySkeleton target,
        GrannyBone bone, out int targetIndex)
    {
        if (target.BonesByName.TryGetValue(bone.Name, out targetIndex)) return true;
        // Some models export a second helper tree at the same bind transforms as
        // the deforming skeleton. Match the full rest transform, not the name or
        // merely the nearest position, to carry that helper with its skinned peer.
        foreach (var candidate in source.Bones)
            if (target.BonesByName.TryGetValue(candidate.Name, out targetIndex) &&
                SameRestTransform(bone.RestWorld, candidate.RestWorld)) return true;
        targetIndex = -1;
        return false;
    }

    private static bool SameRestTransform(Matrix4x4 left, Matrix4x4 right) =>
        Vector3.DistanceSquared(left.Translation, right.Translation) < 1e-8f &&
        Vector3.DistanceSquared(Vector3.TransformNormal(Vector3.UnitX, left), Vector3.TransformNormal(Vector3.UnitX, right)) < 1e-8f &&
        Vector3.DistanceSquared(Vector3.TransformNormal(Vector3.UnitY, left), Vector3.TransformNormal(Vector3.UnitY, right)) < 1e-8f &&
        Vector3.DistanceSquared(Vector3.TransformNormal(Vector3.UnitZ, left), Vector3.TransformNormal(Vector3.UnitZ, right)) < 1e-8f;
}
