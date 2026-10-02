using System.Numerics;

namespace Sacred.Granny.Animation;

/// <summary>Attachment queries require palettes, never a materialized character mesh.</summary>
public static class GrnPoseQueries
{
    public static bool TryTransformRigidPoint(GrnPose pose, string boneName, Vector3 bind, out Vector3 result)
    {
        if (!pose.Data.TryFindBone(boneName, out var index)) { result = bind; return false; }
        if (!pose.IsEvaluated) throw new InvalidOperationException("Evaluate a pose before querying attachments.");
        var projection = pose.Data.Projection;
        result = projection.Project(Vector3.Transform(projection.Unproject(bind), pose.RigidSkinTransforms[index]));
        return true;
    }

    public static bool TryTransformRigidDirection(GrnPose pose, string boneName, Vector3 bind, out Vector3 result)
    {
        if (!pose.Data.TryFindBone(boneName, out var index)) { result = bind; return false; }
        if (!pose.IsEvaluated) throw new InvalidOperationException("Evaluate a pose before querying attachments.");
        var projection = pose.Data.Projection;
        result = projection.ProjectDirection(Vector3.TransformNormal(projection.UnprojectDirection(bind), pose.RigidSkinTransforms[index]));
        return true;
    }
}
