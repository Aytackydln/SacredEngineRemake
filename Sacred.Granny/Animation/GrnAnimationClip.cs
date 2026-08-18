using System.Numerics;

namespace Sacred.Granny.Animation;

public sealed record GrnAnimationClip(
    string Name,
    GrnSkeleton Skeleton,
    GrnTransformTrack?[] Tracks,
    float DurationSeconds);

public sealed record GrnTransformTrack(
    float[] TranslationTimes,
    Vector3[] Translations,
    float[] RotationTimes,
    Quaternion[] Rotations,
    float[] ScaleShearTimes,
    Matrix4x4[] ScaleShears);
