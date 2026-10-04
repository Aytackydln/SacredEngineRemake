using System;
using System.Numerics;

namespace Sacred.Engine.Graphics.Skinning;

/// <summary>Immutable normalized key plus the reference's constant adjacent-key slerp parameters.</summary>
internal readonly record struct GpuPoseRotationKey(Vector4 Value, Vector4 Slerp)
{
    public static Quaternion Normalize(Quaternion q) => q.LengthSquared() > .000001f &&
        float.IsFinite(q.X) && float.IsFinite(q.Y) && float.IsFinite(q.Z) && float.IsFinite(q.W)
        ? Quaternion.Normalize(q) : Quaternion.Identity;
    public static GpuPoseRotationKey[] Prepare(Quaternion[] values)
    {
        var result = new GpuPoseRotationKey[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var q = Normalize(values[i]); var next = Normalize(values[Math.Min(i + 1, values.Length - 1)]);
            var cosine = Quaternion.Dot(q, next); var sign = cosine < 0 ? -1f : 1f; cosine = MathF.Abs(cosine);
            var close = cosine > 1 - .000001f;
            var omega = close ? 0 : MathF.Acos(cosine);
            result[i] = new(new(q.X, q.Y, q.Z, q.W), new(omega, close ? 0 : 1 / MathF.Sin(omega), sign, close ? 1 : 0));
        }
        return result;
    }
}
