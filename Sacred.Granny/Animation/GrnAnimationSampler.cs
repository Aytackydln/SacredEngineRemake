using System.Numerics;

namespace Sacred.Granny.Animation;

internal static class GrnAnimationSampler
{
    public static float WrapTime(float timeSeconds, float durationSeconds)
    {
        if (!float.IsFinite(timeSeconds) || !float.IsFinite(durationSeconds) || durationSeconds <= 0.000001f)
            return 0.0f;

        var wrapped = timeSeconds % durationSeconds;
        return wrapped < 0.0f ? wrapped + durationSeconds : wrapped;
    }

    public static Vector3 SampleVector3(
        IReadOnlyList<float> times,
        IReadOnlyList<Vector3> values,
        float time,
        Vector3 fallback)
    {
        if (values.Count == 0)
            return fallback;
        var (left, right, amount) = FindKeys(times, values.Count, time);
        return left == right ? values[left] : Vector3.Lerp(values[left], values[right], amount);
    }

    public static Quaternion SampleQuaternion(
        IReadOnlyList<float> times,
        IReadOnlyList<Quaternion> values,
        float time,
        Quaternion fallback)
    {
        if (values.Count == 0)
            return NormalizeOrIdentity(fallback);
        var (left, right, amount) = FindKeys(times, values.Count, time);
        var first = NormalizeOrIdentity(values[left]);
        if (left == right)
            return first;
        return Quaternion.Normalize(Quaternion.Slerp(first, NormalizeOrIdentity(values[right]), amount));
    }

    public static Matrix4x4 SampleMatrix(
        IReadOnlyList<float> times,
        IReadOnlyList<Matrix4x4> values,
        float time,
        Matrix4x4 fallback)
    {
        if (values.Count == 0)
            return fallback;
        var (left, right, amount) = FindKeys(times, values.Count, time);
        return left == right ? values[left] : Lerp(values[left], values[right], amount);
    }

    private static (int Left, int Right, float Amount) FindKeys(
        IReadOnlyList<float> times,
        int valueCount,
        float time)
    {
        var count = Math.Min(times.Count, valueCount);
        if (count <= 1 || time <= times[0])
            return (0, 0, 0.0f);
        if (time >= times[count - 1])
            return (count - 1, count - 1, 0.0f);

        var lower = 0;
        var upper = count - 1;
        while (upper - lower > 1)
        {
            var middle = lower + (upper - lower) / 2;
            if (times[middle] <= time)
                lower = middle;
            else
                upper = middle;
        }

        var span = times[upper] - times[lower];
        var amount = float.IsFinite(span) && span > 0.000001f
            ? Math.Clamp((time - times[lower]) / span, 0.0f, 1.0f)
            : 0.0f;
        return (lower, upper, amount);
    }

    public static Matrix4x4 CreateTransform(
        Vector3 translation,
        Quaternion rotation,
        Matrix4x4 scaleShear) =>
        scaleShear *
        Matrix4x4.CreateFromQuaternion(NormalizeOrIdentity(rotation)) *
        Matrix4x4.CreateTranslation(translation);

    private static Quaternion NormalizeOrIdentity(Quaternion value) =>
        value.LengthSquared() > 0.000001f &&
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W)
            ? Quaternion.Normalize(value)
            : Quaternion.Identity;

    private static Matrix4x4 Lerp(Matrix4x4 left, Matrix4x4 right, float amount)
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
