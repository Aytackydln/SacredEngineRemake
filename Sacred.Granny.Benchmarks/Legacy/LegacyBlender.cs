// Verbatim copy of the pre-optimisation implementation (git HEAD~ of the optimisation commit), renamed, as a benchmark baseline.
using System.Numerics;
using Sacred.Granny.Animation;
namespace Sacred.Granny.Benchmarks.Legacy;

internal static class LegacyBlender
{
    public static Matrix4x4 Local(GrnBone skinBone, GrnAnimationClip target, float targetTime, GrnPoseBlend blend)
    {
        var sourceTime = blend.SourceMode == GrnPoseTimeMode.Clamp
            ? Math.Clamp(blend.SourceSeconds, 0, MathF.Max(0, blend.Source.DurationSeconds))
            : LegacySampler.WrapTime(blend.SourceSeconds, blend.Source.DurationSeconds);
        var source = Sample(blend.Source, skinBone, sourceTime);
        var destination = Sample(target, skinBone, targetTime);
        var weight = Math.Clamp(blend.TargetWeight, 0, 1);
        return LegacySampler.CreateTransform(Vector3.Lerp(source.Position, destination.Position, weight),
            Quaternion.Lerp(source.Rotation, destination.Rotation, weight), Matrix4x4.Lerp(source.Scale, destination.Scale, weight));
    }

    private static (Vector3 Position, Quaternion Rotation, Matrix4x4 Scale) Sample(GrnAnimationClip clip, GrnBone fallback, float time)
    {
        if (!clip.Skeleton.TryFindBone(fallback.Name, out var index))
            return (fallback.RestTranslation, fallback.RestRotation, fallback.RestScaleShear);
        var bone = clip.Skeleton.Bones[index];
        var track = index < clip.Tracks.Length ? clip.Tracks[index] : null;
        return (LegacySampler.SampleVector3(track?.TranslationTimes ?? [], track?.Translations ?? [], time, bone.RestTranslation),
            LegacySampler.SampleQuaternion(track?.RotationTimes ?? [], track?.Rotations ?? [], time, bone.RestRotation),
            LegacySampler.SampleMatrix(track?.ScaleShearTimes ?? [], track?.ScaleShears ?? [], time, bone.RestScaleShear));
    }
}
