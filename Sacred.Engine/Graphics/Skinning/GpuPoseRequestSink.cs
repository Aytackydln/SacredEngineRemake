using System;
using Sacred.Granny.Animation;

namespace Sacred.Engine.Graphics.Skinning;

/// <summary>CPU-free request acceptance. The renderer consumes the owner's latest visual request.</summary>
internal sealed class GpuPoseRequestSink : IGrnPoseRequestSink
{
    public static GpuPoseRequestSink Instance { get; } = new();
    public void Submit(in GrnPoseRequest request)
    {
        if (request.TimeMode == GrnPoseTimeMode.Clamp &&
            (!float.IsFinite(request.TimeSeconds) || !float.IsFinite(request.Animation.DurationSeconds)))
            throw new ArgumentOutOfRangeException(nameof(request), "GPU clamped poses require finite time and duration.");
    }
}
