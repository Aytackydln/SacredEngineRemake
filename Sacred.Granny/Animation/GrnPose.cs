using System.Numerics;

namespace Sacred.Granny.Animation;

/// <summary>An instance's current evaluated palettes; spans remain valid only until its next evaluation.</summary>
public sealed class GrnPose
{
    internal GrnPose(GrnSkinningData data)
    {
        Data = data;
        WorldTransforms = new Matrix4x4[data.Bones.Length];
        RegularTransforms = new Matrix4x4[data.Bones.Length];
        RigidTransforms = new Matrix4x4[data.Bones.Length];
    }

    public GrnSkinningData Data { get; }
    public ulong Revision { get; internal set; }
    public bool IsEvaluated => Revision != 0;
    public float SampleTimeSeconds { get; internal set; }
    public GrnAnimationClip? Animation { get; internal set; }
    public ReadOnlySpan<Matrix4x4> BoneWorldTransforms => WorldTransforms;
    public ReadOnlySpan<Matrix4x4> SkinTransforms => RegularTransforms;
    public ReadOnlySpan<Matrix4x4> RigidSkinTransforms => RigidTransforms;
    internal Matrix4x4[] WorldTransforms { get; }
    internal Matrix4x4[] RegularTransforms { get; }
    internal Matrix4x4[] RigidTransforms { get; }
}
