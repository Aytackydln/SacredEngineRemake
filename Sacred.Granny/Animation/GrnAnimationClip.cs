using System.Numerics;

namespace Sacred.Granny.Animation;

public sealed record GrnAnimationClip(
    string Name,
    GrnSkeleton Skeleton,
    GrnTransformTrack?[] Tracks,
    float DurationSeconds)
{
    public float PlaybackSpeedMultiplier { get; init; } = 1;
    /// <summary>Authored motion params[8] from Models.tmp, retained independently of clip duration.</summary>
    public float BowReleaseTailFraction { get; init; }
}

public sealed record GrnTransformTrack(
    float[] TranslationTimes,
    Vector3[] Translations,
    float[] RotationTimes,
    Quaternion[] Rotations,
    float[] ScaleShearTimes,
    Matrix4x4[] ScaleShears);
