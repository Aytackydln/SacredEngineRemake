namespace Sacred.Granny.Animation;

/// <summary>A real evaluated rest pose without an animation/control instance.</summary>
public static class GrnBindPose
{
    public static GrnPose Create(GrnSkinningData data)
    {
        var pose = new GrnPose(data);
        for (var index = 0; index < data.Bones.Length; index++)
        {
            var world = data.Bones[index].RestWorld;
            pose.WorldTransforms[index] = world;
            pose.RegularTransforms[index] = data.GetInverseBindTransform(index) * world;
            pose.RigidTransforms[index] = data.GetInverseRigidBindTransform(index) * GrnRigidTransform.CreateOrOriginal(world);
        }
        pose.Revision = 1;
        return pose;
    }
}
