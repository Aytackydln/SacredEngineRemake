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
    public static Matrix4x4 Local(GrnBone skinBone, GrnAnimationClip target, float targetTime, GrnPoseBlend blend)
    {
        var sourceTime = blend.SourceMode == GrnPoseTimeMode.Clamp
            ? Math.Clamp(blend.SourceSeconds, 0, MathF.Max(0, blend.Source.DurationSeconds))
            : GrnAnimationSampler.WrapTime(blend.SourceSeconds, blend.Source.DurationSeconds);
        var source = Sample(blend.Source, skinBone, sourceTime);
        var destination = Sample(target, skinBone, targetTime);
        var weight = Math.Clamp(blend.TargetWeight, 0, 1);
        return GrnAnimationSampler.CreateTransform(Vector3.Lerp(source.Position, destination.Position, weight),
            Quaternion.Lerp(source.Rotation, destination.Rotation, weight), Matrix4x4.Lerp(source.Scale, destination.Scale, weight));
    }

    private static (Vector3 Position, Quaternion Rotation, Matrix4x4 Scale) Sample(GrnAnimationClip clip, GrnBone fallback, float time)
    {
        if (!clip.Skeleton.TryFindBone(fallback.Name, out var index))
            return (fallback.RestTranslation, fallback.RestRotation, fallback.RestScaleShear);
        var bone = clip.Skeleton.Bones[index];
        var track = index < clip.Tracks.Length ? clip.Tracks[index] : null;
        return (GrnAnimationSampler.SampleVector3(track?.TranslationTimes ?? [], track?.Translations ?? [], time, bone.RestTranslation),
            GrnAnimationSampler.SampleQuaternion(track?.RotationTimes ?? [], track?.Rotations ?? [], time, bone.RestRotation),
            GrnAnimationSampler.SampleMatrix(track?.ScaleShearTimes ?? [], track?.ScaleShears ?? [], time, bone.RestScaleShear));
    }
}
