using System.Numerics;
using System.Runtime.CompilerServices;

namespace Sacred.Granny.Animation;

/// <summary>Allocation-free keyframe sampling over spans. Callers that evaluate the same track
/// repeatedly may pass a key hint; playback advances monotonically, so the previous key almost
/// always brackets the next sample and the binary search is skipped.</summary>
internal static class GrnAnimationSampler
{
    public static float WrapTime(float timeSeconds, float durationSeconds)
    {
        if (!float.IsFinite(timeSeconds) || !float.IsFinite(durationSeconds) || durationSeconds <= 0.000001f)
            return 0.0f;

        var wrapped = timeSeconds % durationSeconds;
        return wrapped < 0.0f ? wrapped + durationSeconds : wrapped;
    }

    public static Vector3 SampleVector3(ReadOnlySpan<float> times, ReadOnlySpan<Vector3> values, float time, in Vector3 fallback)
    {
        var hint = 0;
        return SampleVector3(times, values, time, fallback, ref hint);
    }

    public static Vector3 SampleVector3(ReadOnlySpan<float> times, ReadOnlySpan<Vector3> values, float time, in Vector3 fallback, ref int hint)
    {
        if (values.IsEmpty)
            return fallback;
        FindKeys(times, values.Length, time, ref hint, out var left, out var right, out var amount);
        return left == right ? values[left] : Vector3.Lerp(values[left], values[right], amount);
    }

    public static Quaternion SampleQuaternion(ReadOnlySpan<float> times, ReadOnlySpan<Quaternion> values, float time, in Quaternion fallback)
    {
        var hint = 0;
        return SampleQuaternion(times, values, time, fallback, ref hint);
    }

    public static Quaternion SampleQuaternion(ReadOnlySpan<float> times, ReadOnlySpan<Quaternion> values, float time, in Quaternion fallback, ref int hint)
    {
        if (values.IsEmpty)
            return NormalizeOrIdentity(fallback);
        FindKeys(times, values.Length, time, ref hint, out var left, out var right, out var amount);
        var first = NormalizeOrIdentity(values[left]);
        if (left == right)
            return first;
        return Quaternion.Normalize(Quaternion.Slerp(first, NormalizeOrIdentity(values[right]), amount));
    }

    /// <summary>Same result as <see cref="SampleQuaternion(ReadOnlySpan{float}, ReadOnlySpan{Quaternion}, float, in Quaternion, ref int)"/>
    /// for keys already passed through <see cref="NormalizeOrIdentity"/> (see GrnTransformTrack.NormalizedRotations).</summary>
    public static Quaternion SampleNormalizedQuaternion(ReadOnlySpan<float> times, ReadOnlySpan<Quaternion> normalized, float time, in Quaternion fallback, ref int hint)
    {
        if (normalized.IsEmpty)
            return NormalizeOrIdentity(fallback);
        FindKeys(times, normalized.Length, time, ref hint, out var left, out var right, out var amount);
        return left == right ? normalized[left]
            : Quaternion.Normalize(Quaternion.Slerp(normalized[left], normalized[right], amount));
    }

    public static Matrix4x4 SampleMatrix(ReadOnlySpan<float> times, ReadOnlySpan<Matrix4x4> values, float time, in Matrix4x4 fallback)
    {
        var hint = 0;
        return SampleMatrix(times, values, time, fallback, ref hint);
    }

    public static Matrix4x4 SampleMatrix(ReadOnlySpan<float> times, ReadOnlySpan<Matrix4x4> values, float time, in Matrix4x4 fallback, ref int hint)
    {
        if (values.IsEmpty)
            return fallback;
        FindKeys(times, values.Length, time, ref hint, out var left, out var right, out var amount);
        return left == right ? values[left] : Lerp(values[left], values[right], amount);
    }

    private static void FindKeys(ReadOnlySpan<float> times, int valueCount, float time,
        ref int hint, out int left, out int right, out float amount)
    {
        var count = Math.Min(times.Length, valueCount);
        amount = 0.0f;
        if (count <= 1 || time <= times[0])
        {
            left = right = 0;
            return;
        }
        if (time >= times[count - 1])
        {
            left = right = count - 1;
            return;
        }

        // Same bracket the binary search yields: the last key with times[lower] <= time.
        // Check the previous bracket, then the next one, before searching.
        var lower = hint;
        if ((uint)lower >= (uint)(count - 1) || times[lower] > time || time >= times[lower + 1])
        {
            if ((uint)(lower + 1) < (uint)(count - 1) && times[lower + 1] <= time && time < times[lower + 2])
                lower++;
            else
            {
                lower = 0;
                var high = count - 1;
                while (high - lower > 1)
                {
                    var middle = lower + (high - lower) / 2;
                    if (times[middle] <= time)
                        lower = middle;
                    else
                        high = middle;
                }
            }
        }
        hint = lower;

        var upper = lower + 1;
        var span = times[upper] - times[lower];
        if (float.IsFinite(span) && span > 0.000001f)
            amount = Math.Clamp((time - times[lower]) / span, 0.0f, 1.0f);
        left = lower;
        right = upper;
    }

    public static Matrix4x4 CreateTransform(in Vector3 translation, in Quaternion rotation, in Matrix4x4 scaleShear)
    {
        var result = Matrix4x4.CreateFromQuaternion(NormalizeOrIdentity(rotation));
        if (!scaleShear.IsIdentity)
            return ApplyScaleShear(scaleShear, result, translation);
        // rotation * translation only fills the last row; skip two full matrix products.
        result.Translation = translation;
        return result;
    }

    // Kept out of line so the common identity-scale path stays small and cheap.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Matrix4x4 ApplyScaleShear(in Matrix4x4 scaleShear, in Matrix4x4 rotation, in Vector3 translation)
    {
        var scaled = scaleShear * rotation;
        if (scaled.M14 == 0 && scaled.M24 == 0 && scaled.M34 == 0 && scaled.M44 == 1 &&
            scaled.M41 == 0 && scaled.M42 == 0 && scaled.M43 == 0)
        {
            // Affine with an empty last row (what the sampler's scale/shear lerp guarantees): the product
            // with a pure translation matrix just fills that row, so skip the second matrix product.
            scaled.Translation = translation;
            return scaled;
        }
        return scaled * Matrix4x4.CreateTranslation(translation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Quaternion NormalizeOrIdentity(in Quaternion value) =>
        value.LengthSquared() > 0.000001f &&
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W)
            ? Quaternion.Normalize(value)
            : Quaternion.Identity;

    private static Matrix4x4 Lerp(in Matrix4x4 left, in Matrix4x4 right, float amount)
    {
        // Runtime SIMD implementation, with portable fallback. Preserve the original
        // scale/shear-only semantics even when authored matrices contain other fields.
        var result = Matrix4x4.Lerp(left, right, amount);
        result.M14 = result.M24 = result.M34 = 0;
        result.M41 = result.M42 = result.M43 = 0;
        result.M44 = 1;
        return result;
    }
}
