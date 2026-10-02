using System.Numerics;
using Sacred.Granny.Diagnostics;

namespace Sacred.Granny.Animation;

/// <summary>Samples clips and evaluates hierarchy/palettes without reading or modifying mesh vertices.</summary>
public sealed class GrnPoseEvaluator
{
    private readonly GrnSkinningData _data;
    private readonly int[] _animationBoneBySkinBone;
    private readonly byte[] _worldTransformStates;
    private Matrix4x4[] _animationLocals = [];
    private bool _needsEvaluation = true;

    public GrnPoseEvaluator(GrnSkinningData data, GrnAnimationClip animation)
    {
        _data = data;
        Pose = new GrnPose(data);
        _animationBoneBySkinBone = new int[data.Bones.Length];
        _worldTransformStates = new byte[data.Bones.Length];
        SetAnimation(animation);
    }

    public GrnPose Pose { get; }
    public GrnAnimationClip Animation { get; private set; } = null!;

    public void SetAnimation(GrnAnimationClip animation)
    {
        ArgumentNullException.ThrowIfNull(animation);
        Animation = animation;
        if (_animationLocals.Length != animation.Skeleton.Bones.Length)
            _animationLocals = new Matrix4x4[animation.Skeleton.Bones.Length];
        Array.Fill(_animationBoneBySkinBone, -1);
        for (var i = 0; i < _data.Bones.Length; i++)
        {
            var name = _data.Bones[i].Name;
            if (!string.IsNullOrWhiteSpace(name) && animation.Skeleton.TryFindBone(name, out var index))
                _animationBoneBySkinBone[i] = index;
        }
        _needsEvaluation = true;
    }

    public GrnPose Evaluate(float timeSeconds) => EvaluateSampled(GrnAnimationSampler.WrapTime(timeSeconds, Animation.DurationSeconds));
    public GrnPose EvaluateClamped(float timeSeconds) => EvaluateSampled(Math.Clamp(timeSeconds, 0.0f, MathF.Max(0, Animation.DurationSeconds)));

    private GrnPose EvaluateSampled(float time)
    {
        if (!_needsEvaluation && Pose.SampleTimeSeconds == time) return Pose;
        using (AnimationPerformance.Measure(AnimationCpuStage.Sampling)) SampleAnimationLocals(time);
        using (AnimationPerformance.Measure(AnimationCpuStage.Palettes))
        {
            Array.Clear(_worldTransformStates);
            for (var i = 0; i < Pose.WorldTransforms.Length; i++) ComputeSkinBoneWorldTransform(i);
            for (var i = 0; i < Pose.RegularTransforms.Length; i++)
            {
                Pose.RegularTransforms[i] = _data.GetInverseBindTransform(i) * Pose.WorldTransforms[i];
                Pose.RigidTransforms[i] = _data.GetInverseRigidBindTransform(i) * GrnRigidTransform.CreateOrOriginal(Pose.WorldTransforms[i]);
            }
        }
        Pose.SampleTimeSeconds = time;
        Pose.Animation = Animation;
        Pose.Revision++;
        _needsEvaluation = false;
        return Pose;
    }

    private void SampleAnimationLocals(float timeSeconds)
    {
        var bones = Animation.Skeleton.Bones;
        for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            var bone = bones[boneIndex];
            var track = boneIndex < Animation.Tracks.Length ? Animation.Tracks[boneIndex] : null;
            if (track is null) { _animationLocals[boneIndex] = bone.RestLocal; continue; }
            var translation = GrnAnimationSampler.SampleVector3(track.TranslationTimes, track.Translations, timeSeconds, bone.RestTranslation);
            var rotation = GrnAnimationSampler.SampleQuaternion(track.RotationTimes, track.Rotations, timeSeconds, bone.RestRotation);
            var scaleShear = GrnAnimationSampler.SampleMatrix(track.ScaleShearTimes, track.ScaleShears, timeSeconds, bone.RestScaleShear);
            _animationLocals[boneIndex] = GrnAnimationSampler.CreateTransform(translation, rotation, scaleShear);
        }
    }

    private bool ComputeSkinBoneWorldTransform(int boneIndex)
    {
        if (_worldTransformStates[boneIndex] == 2) return true;
        if (_worldTransformStates[boneIndex] == 1) return false;
        _worldTransformStates[boneIndex] = 1;
        var skinBone = _data.Bones[boneIndex];
        var animationBoneIndex = _animationBoneBySkinBone[boneIndex];
        var local = animationBoneIndex >= 0 ? _animationLocals[animationBoneIndex] : skinBone.RestLocal;
        var parentIndex = skinBone.ParentIndex;
        if (parentIndex == boneIndex) Pose.WorldTransforms[boneIndex] = local;
        else if ((uint)parentIndex >= (uint)Pose.WorldTransforms.Length || !ComputeSkinBoneWorldTransform(parentIndex))
            Pose.WorldTransforms[boneIndex] = skinBone.RestWorld;
        else Pose.WorldTransforms[boneIndex] = local * Pose.WorldTransforms[parentIndex];
        _worldTransformStates[boneIndex] = 2;
        return true;
    }
}
