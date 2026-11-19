using System.Numerics;

namespace Sacred.Granny.Animation;

/// <summary>Another authored clip contributing to a pose. Source time advances
/// independently; TargetWeight is the new clip's weight.</summary>
public readonly record struct GrnPoseBlend(GrnAnimationClip Source, float SourceSeconds, float TargetWeight)
{
    public GrnPoseTimeMode SourceMode { get; init; }
}

internal static class GrnPoseBlender
{
    public static float SourceTime(in GrnPoseBlend blend) => blend.SourceMode == GrnPoseTimeMode.Clamp
        ? Math.Clamp(blend.SourceSeconds, 0, MathF.Max(0, blend.Source.DurationSeconds))
        : GrnAnimationSampler.WrapTime(blend.SourceSeconds, blend.Source.DurationSeconds);

    /// <summary>Bone indices are resolved once per clip pair by the evaluator (-1 = bone absent from that clip).</summary>
    public static Matrix4x4 Local(in GrnBone skinBone, GrnAnimationClip target, int targetIndex, float targetTime,
        GrnAnimationClip source, int sourceIndex, float sourceTime, float weight)
    {
        Sample(source, sourceIndex, skinBone, sourceTime, out var sourcePosition, out var sourceRotation, out var sourceScale);
        Sample(target, targetIndex, skinBone, targetTime, out var targetPosition, out var targetRotation, out var targetScale);
        return GrnAnimationSampler.CreateTransform(Vector3.Lerp(sourcePosition, targetPosition, weight),
            Quaternion.Lerp(sourceRotation, targetRotation, weight), Matrix4x4.Lerp(sourceScale, targetScale, weight));
    }

    private static void Sample(GrnAnimationClip clip, int index, in GrnBone fallback, float time,
        out Vector3 position, out Quaternion rotation, out Matrix4x4 scale)
    {
        if (index < 0)
        {
            position = fallback.RestTranslation;
            rotation = fallback.RestRotation;
            scale = fallback.RestScaleShear;
            return;
        }
        ref readonly var bone = ref clip.Skeleton.Bones[index];
        var track = index < clip.Tracks.Length ? clip.Tracks[index] : null;
        if (track is null)
        {
            position = GrnAnimationSampler.SampleVector3([], [], time, bone.RestTranslation);
            rotation = GrnAnimationSampler.SampleQuaternion([], [], time, bone.RestRotation);
            scale = GrnAnimationSampler.SampleMatrix([], [], time, bone.RestScaleShear);
            return;
        }
        position = GrnAnimationSampler.SampleVector3(track.TranslationTimes, track.Translations, time, bone.RestTranslation);
        var rotationHint = 0;
        rotation = GrnAnimationSampler.SampleNormalizedQuaternion(track.RotationTimes, track.NormalizedRotations, time, bone.RestRotation, ref rotationHint);
        scale = GrnAnimationSampler.SampleMatrix(track.ScaleShearTimes, track.ScaleShears, time, bone.RestScaleShear);
    }
}
