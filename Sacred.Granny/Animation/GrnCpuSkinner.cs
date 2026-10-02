using System.Numerics;
using Sacred.Granny.Diagnostics;
using Sacred.Granny.Meshes;

namespace Sacred.Granny.Animation;

/// <summary>Reference CPU deformation and selected-vertex queries using an already evaluated pose.</summary>
public static class GrnCpuSkinner
{
    public static void Materialize(GrnPose pose, Mesh output)
    {
        if (!pose.IsEvaluated) throw new InvalidOperationException("Evaluate a pose before materializing vertices.");
        if (output.Vertices.Length != pose.Data.Vertices.Length) throw new ArgumentException("Vertex count mismatch.", nameof(output));
        using var measurement = AnimationPerformance.Measure(AnimationCpuStage.Skinning);
        for (var i = 0; i < output.Vertices.Length; i++)
            output.Vertices[i] = TransformVertex(pose, i, output.Vertices[i]);
        output.MarkVerticesChanged();
    }

    public static VertexPositionNormalTexture TransformVertex(GrnPose pose, int index, VertexPositionNormalTexture template)
    {
        if (!pose.IsEvaluated) throw new InvalidOperationException("Evaluate a pose before querying vertices.");
        var data = pose.Data;
        var vertex = data.Vertices[index];
        var transforms = vertex.UsesRigidBoneTransform ? pose.RigidSkinTransforms : pose.SkinTransforms;
        var position = Vector3.Zero;
        var normal = Vector3.Zero;
        var totalWeight = 0.0f;
        foreach (var influence in data.Influences.Slice(vertex.InfluenceOffset, vertex.InfluenceCount))
        {
            if ((uint)influence.BoneIndex >= (uint)transforms.Length ||
                !float.IsFinite(influence.Weight) || influence.Weight <= 0.0f) continue;
            position += Vector3.Transform(vertex.BindPosition, transforms[influence.BoneIndex]) * influence.Weight;
            normal += Vector3.TransformNormal(vertex.BindNormal, transforms[influence.BoneIndex]) * influence.Weight;
            totalWeight += influence.Weight;
        }
        var rawPosition = totalWeight > 0.000001f ? position / totalWeight : vertex.BindPosition;
        var rawNormal = totalWeight > 0.000001f ? normal / totalWeight : vertex.BindNormal;
        var projectedNormal = data.Projection.ProjectDirection(rawNormal);
        projectedNormal = projectedNormal.LengthSquared() > 0.000001f ? Vector3.Normalize(projectedNormal) : Vector3.UnitZ;
        return template with { Position = data.Projection.Project(rawPosition), Normal = projectedNormal };
    }
}
