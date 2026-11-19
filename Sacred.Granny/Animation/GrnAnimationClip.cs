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
    Matrix4x4[] ScaleShears)
{
    private NormalizedKeys? _normalized;

    /// <summary>Rotation keys run through the sampler's normalize-or-identity rule once, so per-sample work can
    /// skip it. Built lazily (benign race: both threads compute identical arrays) and tied to the
    /// <see cref="Rotations"/> array instance, which authored data never mutates after publication.</summary>
    internal ReadOnlySpan<Quaternion> NormalizedRotations
    {
        get
        {
            var cache = _normalized;
            if (cache is null || !ReferenceEquals(cache.Source, Rotations))
                _normalized = cache = new NormalizedKeys(Rotations);
            return cache.Values;
        }
    }

    private sealed class NormalizedKeys
    {
        public NormalizedKeys(Quaternion[] source)
        {
            Source = source;
            Values = new Quaternion[source.Length];
            for (var i = 0; i < source.Length; i++) Values[i] = GrnAnimationSampler.NormalizeOrIdentity(source[i]);
        }

        public Quaternion[] Source { get; }
        public Quaternion[] Values { get; }
    }
}
