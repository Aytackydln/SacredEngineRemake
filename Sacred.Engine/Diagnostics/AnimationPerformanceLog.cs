using System;
using System.Diagnostics;
using System.Linq;
using Sacred.Engine.Graphics.Frames;
using Sacred.Granny.Diagnostics;
using Sacred.Particles.Diagnostics;

namespace Sacred.Engine.Diagnostics;

internal sealed class AnimationPerformanceLog
{
    private long _start;
    private int _frames;
    public Func<GpuAnimationTimingSnapshot>? ReadGpuTimings { get; set; }

    public void Reset()
    {
        _start = 0;
        _frames = 0;
        AnimationPerformance.CaptureAndReset();
        ParticlePerformance.CaptureAndReset();
        ReadGpuTimings?.Invoke();
    }

    public void RecordFrame()
    {
        if (!AnimationPerformance.Enabled)
        {
            _start = 0;
            _frames = 0;
            return;
        }
        if (_start == 0) _start = Stopwatch.GetTimestamp();
        _frames++;
        if (Stopwatch.GetElapsedTime(_start).TotalSeconds < 2) return;
        var cpu = AnimationPerformance.CaptureAndReset();
        var cpuStages = string.Join(", ", Enum.GetValues<AnimationCpuStage>().Select(stage =>
            $"{stage} {cpu.Milliseconds[(int)stage] / _frames:0.000} ms/frame ({cpu.Calls[(int)stage]} calls, {cpu.AllocatedBytes[(int)stage] / (double)_frames:0} allocated bytes/frame)"));
        EngineLog.WriteLine($"Animation CPU: {cpuStages}; vertex uploads {cpu.UploadedVertexBytes / (double)_frames:0} bytes/frame; palette uploads {cpu.UploadedPaletteBytes / (double)_frames:0} bytes/frame; particle uploads {cpu.UploadedParticleBytes / (double)_frames:0} bytes/frame; {_frames} frames.");
        var particle = ParticlePerformance.CaptureAndReset();
        var particleStages = string.Join(", ", Enum.GetValues<ParticleCpuStage>().Select(stage =>
            $"{stage} {particle.Milliseconds[(int)stage] / _frames:0.000} ms/frame ({particle.Calls[(int)stage]} calls, {particle.AllocatedBytes[(int)stage] / (double)_frames:0} allocated bytes/frame)"));
        EngineLog.WriteLine($"Particle CPU work: {particleStages}; births {particle.Births}; {_frames} frames; nested scopes overlap.");
        if (ReadGpuTimings?.Invoke() is { } gpu)
        {
            var stages = string.Join(", ", Enum.GetValues<AnimationGpuStage>().Select(stage =>
                gpu.Samples[(int)stage] == 0 ? $"{stage} n/a" : $"{stage} {gpu.Milliseconds[(int)stage] / Math.Max(1, gpu.CompletedFrames):0.000} ms/frame"));
            EngineLog.WriteLine($"Animation GPU: {stages}; {gpu.CompletedFrames} completed frames, {gpu.OmittedScopes} omitted scopes.");
        }
        _start = 0;
        _frames = 0;
    }
}
