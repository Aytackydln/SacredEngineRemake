using System.Numerics;
using Sacred.Granny.Diagnostics;

namespace Sacred.Granny.Animation;

/// <summary>Samples clips and evaluates hierarchy/palettes without reading or modifying mesh vertices.</summary>
public sealed class GrnPoseEvaluator
{
    private readonly GrnSkinningData _data;
    private readonly int[] _animationBoneBySkinBone;
    private readonly GrnPoseHierarchy.Step[] _hierarchy;
    private Matrix4x4[] _animationLocals = [];
    // Last bracketing key per bone channel (translation, rotation, scale/shear).
    private int[] _keyHints = [];
    private readonly int[] _blendSourceBoneBySkinBone;
    private GrnAnimationClip? _blendSource;
    private bool _needsEvaluation = true;
    private GrnPoseBlend? _blend;

    public GrnPoseEvaluator(GrnSkinningData data, GrnAnimationClip animation)
    {
        _data = data;
        Pose = new GrnPose(data);
        _animationBoneBySkinBone = new int[data.Bones.Length];
        _blendSourceBoneBySkinBone = new int[data.Bones.Length];
        _hierarchy = data.Hierarchy;
        SetAnimation(animation);
    }

    public GrnPose Pose { get; }
    internal void InvalidateSample() => _needsEvaluation = true;
    public GrnAnimationClip Animation { get; private set; } = null!;

    public void SetAnimation(GrnAnimationClip animation)
    {
        ArgumentNullException.ThrowIfNull(animation);
        Animation = animation;
        if (_animationLocals.Length != animation.Skeleton.Bones.Length)
        {
            _animationLocals = new Matrix4x4[animation.Skeleton.Bones.Length];
            _keyHints = new int[animation.Skeleton.Bones.Length * 3];
        }
        else Array.Clear(_keyHints);
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
    public GrnPose EvaluateBlended(float seconds, GrnPoseTimeMode mode, GrnPoseBlend blend)
    {
        _blend = blend;
        _needsEvaluation = true;
        try { return mode == GrnPoseTimeMode.Clamp ? EvaluateClamped(seconds) : Evaluate(seconds); }
        finally { _blend = null; _needsEvaluation = true; }
    }

    private GrnPose EvaluateSampled(float time)
    {
        if (!_needsEvaluation && Pose.SampleTimeSeconds == time) return Pose;
        // Blending samples both clips directly in skin-bone order. Sampling the
        // destination skeleton here as well would discard an entire pose's work.
        if (_blend is null)
            using (AnimationPerformance.Measure(AnimationCpuStage.Sampling)) SampleAnimationLocals(time);
        using (AnimationPerformance.Measure(AnimationCpuStage.Palettes))
        {
            Pose.SampleTimeSeconds = time;
            var bones = _data.Bones;
            var world = Pose.WorldTransforms.AsSpan();
            var locals = _animationLocals.AsSpan();
            var animationBones = _animationBoneBySkinBone.AsSpan();
            var steps = _hierarchy.AsSpan();
            var blend = _blend;
            var sourceTime = 0f;
            var sourceBones = default(ReadOnlySpan<int>);
            var weight = 0f;
            if (blend is { } active)
            {
                sourceBones = BlendSourceBones(active.Source);
                sourceTime = GrnPoseBlender.SourceTime(active);
                weight = Math.Clamp(active.TargetWeight, 0, 1);
            }
            foreach (ref readonly var step in steps)
            {
                var i = step.Bone;
                ref readonly var bone = ref bones[i];
                if (step.RestWorld) { world[i] = bone.RestWorld; continue; }
                var animationBone = animationBones[i];
                Matrix4x4 local;
                if (blend is { } b)
                    local = GrnPoseBlender.Local(bone, Animation, animationBone, time, b.Source, sourceBones[i], sourceTime, weight);
                else
                    local = animationBone >= 0 ? locals[animationBone] : bone.RestLocal;
                world[i] = bone.ParentIndex == i ? local : local * world[bone.ParentIndex];
            }
            var regular = Pose.RegularTransforms.AsSpan();
            var rigid = Pose.RigidTransforms.AsSpan();
            var inverseBind = _data.InverseBindTransforms;
            var inverseRigidBind = _data.InverseRigidBindTransforms;
            for (var i = 0; i < regular.Length; i++)
            {
                regular[i] = inverseBind[i] * world[i];
                rigid[i] = inverseRigidBind[i] * GrnRigidTransform.CreateOrOriginal(world[i]);
            }
        }
        Pose.SampleTimeSeconds = time;
        Pose.Animation = Animation;
        Pose.Revision++;
        _needsEvaluation = false;
        return Pose;
    }

    /// <summary>Skin-bone to source-clip-bone map; rebuilt only when the blend source clip changes.</summary>
    private ReadOnlySpan<int> BlendSourceBones(GrnAnimationClip source)
    {
        if (ReferenceEquals(_blendSource, source)) return _blendSourceBoneBySkinBone;
        _blendSource = source;
        Array.Fill(_blendSourceBoneBySkinBone, -1);
        var bones = _data.Bones;
        for (var i = 0; i < bones.Length; i++)
        {
            var name = bones[i].Name;
            if (!string.IsNullOrWhiteSpace(name) && source.Skeleton.TryFindBone(name, out var index))
                _blendSourceBoneBySkinBone[i] = index;
        }
        return _blendSourceBoneBySkinBone;
    }

    private void SampleAnimationLocals(float timeSeconds)
    {
        var bones = Animation.Skeleton.Bones.AsSpan();
        var tracks = Animation.Tracks.AsSpan();
        var locals = _animationLocals.AsSpan();
        var hints = _keyHints.AsSpan();
        for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            ref readonly var bone = ref bones[boneIndex];
            var track = boneIndex < tracks.Length ? tracks[boneIndex] : null;
            if (track is null) { locals[boneIndex] = bone.RestLocal; continue; }
            var h = boneIndex * 3;
            var translation = GrnAnimationSampler.SampleVector3(track.TranslationTimes, track.Translations, timeSeconds, bone.RestTranslation, ref hints[h]);
            var rotation = GrnAnimationSampler.SampleNormalizedQuaternion(track.RotationTimes, track.NormalizedRotations, timeSeconds, bone.RestRotation, ref hints[h + 1]);
            var scaleShear = GrnAnimationSampler.SampleMatrix(track.ScaleShearTimes, track.ScaleShears, timeSeconds, bone.RestScaleShear, ref hints[h + 2]);
            locals[boneIndex] = GrnAnimationSampler.CreateTransform(translation, rotation, scaleShear);
        }
    }

}
